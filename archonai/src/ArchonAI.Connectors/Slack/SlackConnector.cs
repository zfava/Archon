using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArchonAI.Common.Observability;
using ObsTelemetry = ArchonAI.Common.Observability.Telemetry;
using ArchonAI.Connectors.Framework;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Connectors.Slack;

public sealed class SlackConnector : ISlackConnector, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IEventBus _eventBus;
    private readonly ILogger<SlackConnector> _logger;
    private readonly SlackOptions _options;
    private readonly ConnectorShadowMetrics _shadowMetrics;

    private string? _teamId;
    private string? _teamName;
    private string? _botUserId;
    private DateTimeOffset? _lastAuthenticatedAtUtc;
    private bool _isAuthenticated;

    private long _totalMessagesSent;
    private long _totalMessagesRead;
    private long _failedRequests;
    private long _webhookEventsProcessed;
    private int _rateLimitRemaining = int.MaxValue;

    public SlackConnector(
        HttpClient httpClient,
        IEventBus eventBus,
        ILogger<SlackConnector> logger,
        IOptions<SlackOptions> options,
        ConnectorShadowMetrics? shadowMetrics = null)
    {
        _httpClient = httpClient;
        _eventBus = eventBus;
        _logger = logger;
        _options = options.Value;
        _shadowMetrics = shadowMetrics ?? new ConnectorShadowMetrics();
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds);
    }

    public string SystemName => "slack";

    public async global::System.Threading.Tasks.Task<SlackAuthResult> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("Slack.Authenticate");

        try
        {
            ObsTelemetry.SlackAuthAttempts.Add(1, new KeyValuePair<string, object?>("result", "attempt"));

            var response = await ExecuteWithRetryAsync(
                () => SendAuthorizedRequestAsync(HttpMethod.Get, $"{_options.BaseUrl}/auth.test", null, cancellationToken),
                cancellationToken);

            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

            if (!body.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                string error = body.TryGetProperty("error", out var err)
                    ? err.GetString() ?? "Authentication failed"
                    : "Authentication failed";

                _logger.LogError("Slack auth.test failed: {Error}", error);
                ObsTelemetry.SlackAuthAttempts.Add(1, new KeyValuePair<string, object?>("result", "failure"));
                return new SlackAuthResult(false, null, null, null, null, error);
            }

            _teamId = body.TryGetProperty("team_id", out var tid) ? tid.GetString() : null;
            _teamName = body.TryGetProperty("team", out var tn) ? tn.GetString() : null;
            _botUserId = body.TryGetProperty("user_id", out var uid) ? uid.GetString() : null;
            _lastAuthenticatedAtUtc = DateTimeOffset.UtcNow;
            _isAuthenticated = true;

            _logger.LogInformation("Slack authenticated for team {TeamName} ({TeamId})", _teamName, _teamId);
            ObsTelemetry.SlackAuthAttempts.Add(1, new KeyValuePair<string, object?>("result", "success"));

            return new SlackAuthResult(true, _teamId, _teamName, _botUserId, _lastAuthenticatedAtUtc, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Slack authentication failed with exception");
            ObsTelemetry.SlackAuthAttempts.Add(1, new KeyValuePair<string, object?>("result", "failure"));
            Interlocked.Increment(ref _failedRequests);
            return new SlackAuthResult(false, null, null, null, null, ex.Message);
        }
    }

    public async global::System.Threading.Tasks.Task<SlackMessageResult> SendMessageAsync(
        string channel, string text, string? threadTs = null, CancellationToken cancellationToken = default)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("Slack.SendMessage");
        activity?.SetTag("slack.channel", channel);

        if (string.IsNullOrWhiteSpace(channel))
            throw new ArgumentException("Channel is required.", nameof(channel));
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Message text is required.", nameof(text));

        await EnsureAuthenticatedAsync(cancellationToken);

        var payload = new Dictionary<string, object>
        {
            ["channel"] = channel,
            ["text"] = text
        };

        if (!string.IsNullOrWhiteSpace(threadTs))
        {
            payload["thread_ts"] = threadTs;
        }

        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Post, $"{_options.BaseUrl}/chat.postMessage",
                JsonContent.Create(payload), cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        if (!body.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            string error = body.TryGetProperty("error", out var err)
                ? err.GetString() ?? "Message send failed"
                : "Message send failed";

            _logger.LogWarning("Slack chat.postMessage failed: {Error}", error);
            return new SlackMessageResult(false, channel, null, error);
        }

        string? ts = body.TryGetProperty("ts", out var tsProp) ? tsProp.GetString() : null;

        Interlocked.Increment(ref _totalMessagesSent);
        ObsTelemetry.SlackMessagesSent.Add(1, new KeyValuePair<string, object?>("channel", channel));

        _logger.LogInformation("Slack message sent to {Channel}, ts={Ts}", channel, ts);
        await EmitAuditEventAsync("slack.message.sent", "Message", ts ?? string.Empty, cancellationToken);

        return new SlackMessageResult(true, channel, ts, null);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<SlackChannelInfo>> GetChannelsAsync(
        int limit = 100, CancellationToken cancellationToken = default)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("Slack.GetChannels");

        await EnsureAuthenticatedAsync(cancellationToken);

        string url = $"{_options.BaseUrl}/conversations.list?types=public_channel,private_channel&limit={Math.Clamp(limit, 1, 1000)}";

        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, url, null, cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var channels = new List<SlackChannelInfo>();

        if (body.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
            body.TryGetProperty("channels", out var channelArray))
        {
            foreach (var ch in channelArray.EnumerateArray())
            {
                string id = ch.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
                string name = ch.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
                bool isPrivate = ch.TryGetProperty("is_private", out var privProp) && privProp.GetBoolean();
                int memberCount = ch.TryGetProperty("num_members", out var memProp) ? memProp.GetInt32() : 0;

                string? topic = ch.TryGetProperty("topic", out var topicObj) &&
                                topicObj.TryGetProperty("value", out var topicVal)
                    ? topicVal.GetString()
                    : null;

                string? purpose = ch.TryGetProperty("purpose", out var purposeObj) &&
                                  purposeObj.TryGetProperty("value", out var purposeVal)
                    ? purposeVal.GetString()
                    : null;

                channels.Add(new SlackChannelInfo(id, name, isPrivate, memberCount, topic, purpose));
            }
        }

        ObsTelemetry.SlackQueryOps.Add(1, new KeyValuePair<string, object?>("operation", "get_channels"));
        _logger.LogInformation("Slack conversations.list returned {Count} channels", channels.Count);

        return channels;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<SlackMessage>> ReadChannelHistoryAsync(
        string channel, int limit = 50, string? oldest = null, string? latest = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("Slack.ReadChannelHistory");
        activity?.SetTag("slack.channel", channel);

        if (string.IsNullOrWhiteSpace(channel))
            throw new ArgumentException("Channel is required.", nameof(channel));

        await EnsureAuthenticatedAsync(cancellationToken);

        string url = $"{_options.BaseUrl}/conversations.history?channel={Uri.EscapeDataString(channel)}&limit={Math.Clamp(limit, 1, 1000)}";
        if (!string.IsNullOrWhiteSpace(oldest))
            url += $"&oldest={Uri.EscapeDataString(oldest)}";
        if (!string.IsNullOrWhiteSpace(latest))
            url += $"&latest={Uri.EscapeDataString(latest)}";

        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, url, null, cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var messages = new List<SlackMessage>();

        if (body.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
            body.TryGetProperty("messages", out var msgArray))
        {
            foreach (var msg in msgArray.EnumerateArray())
            {
                string ts = msg.TryGetProperty("ts", out var tsProp) ? tsProp.GetString() ?? string.Empty : string.Empty;
                string? user = msg.TryGetProperty("user", out var userProp) ? userProp.GetString() : null;
                string text = msg.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? string.Empty : string.Empty;
                string? threadTs = msg.TryGetProperty("thread_ts", out var threadProp) ? threadProp.GetString() : null;

                // Parse Slack ts (Unix timestamp with microseconds) to DateTimeOffset
                DateTimeOffset sentAt = DateTimeOffset.UtcNow;
                if (double.TryParse(ts, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double tsDouble))
                {
                    sentAt = DateTimeOffset.FromUnixTimeSeconds((long)tsDouble);
                }

                messages.Add(new SlackMessage(ts, user, text, threadTs, sentAt));
            }
        }

        Interlocked.Add(ref _totalMessagesRead, messages.Count);
        ObsTelemetry.SlackQueryOps.Add(1, new KeyValuePair<string, object?>("operation", "read_history"));

        _logger.LogInformation("Slack conversations.history for {Channel} returned {Count} messages", channel, messages.Count);

        return messages;
    }

    public async global::System.Threading.Tasks.Task<SlackMessageResult> PostAlertAsync(
        string channel, string alertLevel, string title, string details,
        CancellationToken cancellationToken = default)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("Slack.PostAlert");
        activity?.SetTag("slack.alert_level", alertLevel);

        if (string.IsNullOrWhiteSpace(channel))
            channel = _options.DefaultAlertChannel;

        if (string.IsNullOrWhiteSpace(channel))
            throw new ArgumentException("Alert channel is required. Set DefaultAlertChannel in options or provide a channel.", nameof(channel));

        string emoji = alertLevel.ToLowerInvariant() switch
        {
            "critical" => ":rotating_light:",
            "warning" => ":warning:",
            "info" => ":information_source:",
            _ => ":bell:"
        };

        string formattedText = $"{emoji} *[{alertLevel.ToUpperInvariant()}]* {title}\n{details}";

        var result = await SendMessageAsync(channel, formattedText, null, cancellationToken);

        if (result.IsSuccess)
        {
            ObsTelemetry.SlackAlertsSent.Add(1,
                new KeyValuePair<string, object?>("level", alertLevel),
                new KeyValuePair<string, object?>("channel", channel));

            await EmitAuditEventAsync("slack.alert.posted", "Alert", result.Timestamp ?? string.Empty, cancellationToken);
        }

        return result;
    }

    public async global::System.Threading.Tasks.Task<bool> ProcessWebhookEventAsync(
        string requestBody, string signature, string timestamp,
        CancellationToken cancellationToken = default)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("Slack.ProcessWebhookEvent");

        if (!VerifySlackSignature(requestBody, signature, timestamp))
        {
            _logger.LogWarning("Slack webhook signature verification failed");
            ObsTelemetry.SlackErrors.Add(1, new KeyValuePair<string, object?>("reason", "invalid_signature"));
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<JsonElement>(requestBody);

            // Handle URL verification challenge
            if (payload.TryGetProperty("type", out var typeProp) &&
                typeProp.GetString() == "url_verification")
            {
                _logger.LogInformation("Slack URL verification challenge received");
                Interlocked.Increment(ref _webhookEventsProcessed);
                return true;
            }

            // Handle event callbacks
            if (payload.TryGetProperty("event", out var eventData))
            {
                string eventType = eventData.TryGetProperty("type", out var evType)
                    ? evType.GetString() ?? "unknown"
                    : "unknown";

                _logger.LogInformation("Slack webhook event received: {EventType}", eventType);

                var eventPayload = new Dictionary<string, string>
                {
                    ["eventType"] = eventType,
                    ["timestamp"] = timestamp
                };

                if (eventData.TryGetProperty("channel", out var chProp))
                    eventPayload["channel"] = chProp.GetString() ?? string.Empty;
                if (eventData.TryGetProperty("user", out var userProp))
                    eventPayload["user"] = userProp.GetString() ?? string.Empty;
                if (eventData.TryGetProperty("text", out var textProp))
                    eventPayload["text"] = textProp.GetString() ?? string.Empty;

                await EmitAuditEventAsync($"slack.event.{eventType}", "WebhookEvent", timestamp, cancellationToken);

                Interlocked.Increment(ref _webhookEventsProcessed);
                ObsTelemetry.SlackWebhookEvents.Add(1, new KeyValuePair<string, object?>("event_type", eventType));

                return true;
            }

            _logger.LogWarning("Slack webhook received unknown payload type");
            return false;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Slack webhook payload");
            ObsTelemetry.SlackErrors.Add(1, new KeyValuePair<string, object?>("reason", "parse_error"));
            return false;
        }
    }

    public SlackConnectorStatus GetStatus()
    {
        return new SlackConnectorStatus(
            IsConnected: _isAuthenticated,
            TeamId: _teamId,
            TeamName: _teamName,
            LastAuthenticatedAtUtc: _lastAuthenticatedAtUtc,
            TotalMessagesSent: Interlocked.Read(ref _totalMessagesSent),
            TotalMessagesRead: Interlocked.Read(ref _totalMessagesRead),
            FailedRequests: Interlocked.Read(ref _failedRequests),
            WebhookEventsProcessed: Interlocked.Read(ref _webhookEventsProcessed),
            RateLimitRemaining: _rateLimitRemaining,
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task PushResultAsync(ExecutionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Slack connector received execution result for task {TaskId}", result.TaskId);

        await EmitAuditEventAsync("slack.result.pushed", "ExecutionResult", result.TaskId.ToString(), cancellationToken);
    }

    public void Dispose()
    {
        // No semaphore or other disposable resources in this connector
    }

    // --- Private helpers ---

    private bool VerifySlackSignature(string requestBody, string signature, string timestamp)
    {
        if (string.IsNullOrWhiteSpace(_options.SigningSecret))
        {
            _logger.LogWarning("Slack signing secret not configured, skipping signature verification");
            return true;
        }

        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(timestamp))
            return false;

        // Guard against replay attacks — reject timestamps older than 5 minutes
        if (long.TryParse(timestamp, out long ts))
        {
            var eventTime = DateTimeOffset.FromUnixTimeSeconds(ts);
            if (Math.Abs((DateTimeOffset.UtcNow - eventTime).TotalMinutes) > 5)
            {
                _logger.LogWarning("Slack webhook timestamp too old, possible replay attack");
                return false;
            }
        }

        string baseString = $"v0:{timestamp}:{requestBody}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SigningSecret));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(baseString));
        string computed = "v0=" + Convert.ToHexStringLower(hash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(signature));
    }

    private async global::System.Threading.Tasks.Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (_isAuthenticated)
            return;

        var result = await AuthenticateAsync(cancellationToken);
        if (!result.IsAuthenticated)
        {
            throw new InvalidOperationException($"Slack authentication failed: {result.Error}");
        }
    }

    private async global::System.Threading.Tasks.Task<HttpResponseMessage> SendAuthorizedRequestAsync(
        HttpMethod method, string url, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.BotToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (content is not null)
        {
            request.Content = content;
        }

        var sw = Stopwatch.StartNew();
        var response = await _httpClient.SendAsync(request, cancellationToken);
        sw.Stop();
        UpdateRateLimitInfo(response);

        bool success = response.IsSuccessStatusCode;
        _shadowMetrics.RecordRequest(SystemName, success, sw.Elapsed.TotalMilliseconds);

        if (!success)
        {
            Interlocked.Increment(ref _failedRequests);
            ObsTelemetry.SlackErrors.Add(1, new KeyValuePair<string, object?>("status_code", (int)response.StatusCode));

            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Slack API returned {StatusCode}: {Body}", response.StatusCode, errorBody);
        }

        return response;
    }

    private async global::System.Threading.Tasks.Task<HttpResponseMessage> ExecuteWithRetryAsync(
        Func<global::System.Threading.Tasks.Task<HttpResponseMessage>> operation,
        CancellationToken cancellationToken)
    {
        int attempt = 0;
        while (true)
        {
            attempt++;
            var response = await operation();

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            bool isRetryable = response.StatusCode is HttpStatusCode.TooManyRequests
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout
                or HttpStatusCode.RequestTimeout;

            if (!isRetryable || attempt >= _options.MaxRetries)
            {
                response.EnsureSuccessStatusCode();
            }

            int delayMs = _options.RetryBaseDelayMs * (1 << (attempt - 1));

            // Slack uses Retry-After header for rate limiting
            if (response.StatusCode == HttpStatusCode.TooManyRequests &&
                response.Headers.RetryAfter?.Delta is { } retryAfter)
            {
                delayMs = Math.Max(delayMs, (int)retryAfter.TotalMilliseconds);
            }

            _logger.LogWarning(
                "Slack request failed with {StatusCode}, retrying in {DelayMs}ms (attempt {Attempt}/{MaxRetries})",
                response.StatusCode, delayMs, attempt, _options.MaxRetries);

            ObsTelemetry.SlackRetries.Add(1);

            await global::System.Threading.Tasks.Task.Delay(delayMs, cancellationToken);
        }
    }

    private void UpdateRateLimitInfo(HttpResponseMessage response)
    {
        // Slack uses X-RateLimit-Remaining header
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var values))
        {
            string? header = values.FirstOrDefault();
            if (header is not null && int.TryParse(header, out int remaining))
            {
                _rateLimitRemaining = remaining;
                ObsTelemetry.SlackRateLimitRemaining.Record(remaining);

                if (remaining < 20)
                {
                    _logger.LogWarning("Slack rate limit approaching: {Remaining} remaining", remaining);
                }
            }
        }
    }

    private async global::System.Threading.Tasks.Task EmitAuditEventAsync(
        string eventType, string objectType, string recordId, CancellationToken cancellationToken)
    {
        var auditEvent = new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: eventType,
            Source: SystemName,
            CorrelationId: Guid.NewGuid(),
            Payload: new Dictionary<string, string>
            {
                ["objectType"] = objectType,
                ["recordId"] = recordId,
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
            },
            OccurredAtUtc: DateTimeOffset.UtcNow);

        await _eventBus.PublishAsync(auditEvent, cancellationToken);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Connectors.HubSpot;

public sealed class HubSpotConnector : IHubSpotConnector, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IEventBus _eventBus;
    private readonly ILogger<HubSpotConnector> _logger;
    private readonly HubSpotOptions _options;

    private string? _accessToken;
    private string? _portalId;
    private DateTimeOffset? _tokenExpiresAtUtc;
    private DateTimeOffset? _lastAuthenticatedAtUtc;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    private long _totalRequests;
    private long _failedRequests;
    private int _dailyRateLimitRemaining = int.MaxValue;

    public HubSpotConnector(
        HttpClient httpClient,
        IEventBus eventBus,
        ILogger<HubSpotConnector> logger,
        IOptions<HubSpotOptions> options)
    {
        _httpClient = httpClient;
        _eventBus = eventBus;
        _logger = logger;
        _options = options.Value;
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds);
    }

    public string SystemName => "hubspot";

    public async global::System.Threading.Tasks.Task<HubSpotAuthResult> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        await _authLock.WaitAsync(cancellationToken);
        try
        {
            return await AuthenticateCoreAsync(cancellationToken);
        }
        finally
        {
            _authLock.Release();
        }
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<HubSpotRecord>> GetContactsAsync(
        string? filter, int limit = 100, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("HubSpot.GetContacts");

        await EnsureAuthenticatedAsync(cancellationToken);

        string url = $"{_options.BaseUrl}/crm/v3/objects/contacts?limit={Math.Clamp(limit, 1, 100)}";
        if (!string.IsNullOrWhiteSpace(filter))
        {
            url += $"&properties={Uri.EscapeDataString(filter)}";
        }

        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, url, null, cancellationToken),
            cancellationToken);

        var records = await ParseCrmResponseAsync("Contact", response, cancellationToken);

        _logger.LogInformation("HubSpot query returned {Count} contacts", records.Count);
        Telemetry.HubSpotQueryOps.Add(1, new KeyValuePair<string, object?>("object_type", "Contact"));

        return records;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<HubSpotRecord>> GetDealsAsync(
        string? filter, int limit = 100, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("HubSpot.GetDeals");

        await EnsureAuthenticatedAsync(cancellationToken);

        string url = $"{_options.BaseUrl}/crm/v3/objects/deals?limit={Math.Clamp(limit, 1, 100)}";
        if (!string.IsNullOrWhiteSpace(filter))
        {
            url += $"&properties={Uri.EscapeDataString(filter)}";
        }

        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, url, null, cancellationToken),
            cancellationToken);

        var records = await ParseCrmResponseAsync("Deal", response, cancellationToken);

        _logger.LogInformation("HubSpot query returned {Count} deals", records.Count);
        Telemetry.HubSpotQueryOps.Add(1, new KeyValuePair<string, object?>("object_type", "Deal"));

        return records;
    }

    public async global::System.Threading.Tasks.Task<string> UpdatePipelineRecordAsync(
        string objectType, string recordId, IReadOnlyDictionary<string, string> properties,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("HubSpot.UpdatePipelineRecord");
        activity?.SetTag("hs.object_type", objectType);
        activity?.SetTag("hs.record_id", recordId);

        await EnsureAuthenticatedAsync(cancellationToken);

        string apiObjectType = NormalizeObjectType(objectType);
        string url = $"{_options.BaseUrl}/crm/v3/objects/{Uri.EscapeDataString(apiObjectType)}/{Uri.EscapeDataString(recordId)}";

        var payload = new { properties };

        await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(new HttpMethod("PATCH"), url, JsonContent.Create(payload), cancellationToken),
            cancellationToken);

        _logger.LogInformation("Updated HubSpot {ObjectType} record {RecordId}", objectType, recordId);
        Telemetry.HubSpotWriteOps.Add(1,
            new KeyValuePair<string, object?>("operation", "update"),
            new KeyValuePair<string, object?>("object_type", objectType));

        await EmitAuditEventAsync("hubspot.record.updated", objectType, recordId, cancellationToken);

        return recordId;
    }

    public async global::System.Threading.Tasks.Task<string> CreateRecordAsync(
        string objectType, IReadOnlyDictionary<string, string> properties,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("HubSpot.CreateRecord");
        activity?.SetTag("hs.object_type", objectType);

        await EnsureAuthenticatedAsync(cancellationToken);

        string apiObjectType = NormalizeObjectType(objectType);
        string url = $"{_options.BaseUrl}/crm/v3/objects/{Uri.EscapeDataString(apiObjectType)}";

        var payload = new { properties };

        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Post, url, JsonContent.Create(payload), cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        string recordId = body.GetProperty("id").GetString() ?? string.Empty;

        _logger.LogInformation("Created HubSpot {ObjectType} record {RecordId}", objectType, recordId);
        Telemetry.HubSpotWriteOps.Add(1,
            new KeyValuePair<string, object?>("operation", "create"),
            new KeyValuePair<string, object?>("object_type", objectType));

        await EmitAuditEventAsync("hubspot.record.created", objectType, recordId, cancellationToken);

        return recordId;
    }

    public HubSpotConnectorStatus GetStatus()
    {
        return new HubSpotConnectorStatus(
            IsConnected: _accessToken is not null && (_tokenExpiresAtUtc is null || _tokenExpiresAtUtc > DateTimeOffset.UtcNow),
            PortalId: _portalId,
            LastAuthenticatedAtUtc: _lastAuthenticatedAtUtc,
            TokenExpiresAtUtc: _tokenExpiresAtUtc,
            TotalRequests: Interlocked.Read(ref _totalRequests),
            FailedRequests: Interlocked.Read(ref _failedRequests),
            DailyRateLimitRemaining: _dailyRateLimitRemaining,
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task PushResultAsync(ExecutionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("HubSpot connector received execution result for task {TaskId}", result.TaskId);

        await EmitAuditEventAsync("hubspot.result.pushed", "ExecutionResult", result.TaskId.ToString(), cancellationToken);
    }

    public void Dispose()
    {
        _authLock.Dispose();
    }

    // --- Private helpers ---

    private async global::System.Threading.Tasks.Task<HubSpotAuthResult> AuthenticateCoreAsync(CancellationToken cancellationToken)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("HubSpot.Authenticate");

        try
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["refresh_token"] = _options.RefreshToken
            });

            var response = await _httpClient.PostAsync(_options.OAuthTokenUrl, form, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string error = body.TryGetProperty("message", out var msg)
                    ? msg.GetString() ?? "Unknown error"
                    : "Authentication failed";

                _logger.LogError("HubSpot OAuth failed: {Error}", error);
                Interlocked.Increment(ref _failedRequests);
                return new HubSpotAuthResult(false, null, null, error);
            }

            _accessToken = body.GetProperty("access_token").GetString();

            int expiresIn = body.TryGetProperty("expires_in", out var exp)
                ? exp.GetInt32()
                : 1800;

            _lastAuthenticatedAtUtc = DateTimeOffset.UtcNow;
            _tokenExpiresAtUtc = _lastAuthenticatedAtUtc.Value.AddSeconds(expiresIn);

            if (body.TryGetProperty("hub_id", out var hubId))
            {
                _portalId = hubId.ToString();
            }

            _logger.LogInformation("HubSpot OAuth succeeded for portal {PortalId}", _portalId);
            Telemetry.HubSpotAuthAttempts.Add(1, new KeyValuePair<string, object?>("result", "success"));

            return new HubSpotAuthResult(true, _portalId, _tokenExpiresAtUtc, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HubSpot OAuth failed with exception");
            Telemetry.HubSpotAuthAttempts.Add(1, new KeyValuePair<string, object?>("result", "failure"));
            Interlocked.Increment(ref _failedRequests);
            return new HubSpotAuthResult(false, null, null, ex.Message);
        }
    }

    private async global::System.Threading.Tasks.Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _tokenExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return;
        }

        var result = await AuthenticateAsync(cancellationToken);
        if (!result.IsAuthenticated)
        {
            throw new InvalidOperationException($"HubSpot authentication failed: {result.Error}");
        }
    }

    private async global::System.Threading.Tasks.Task<IReadOnlyList<HubSpotRecord>> ParseCrmResponseAsync(
        string objectType, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var records = new List<HubSpotRecord>();

        if (body.TryGetProperty("results", out var resultsElement))
        {
            foreach (var item in resultsElement.EnumerateArray())
            {
                string id = item.TryGetProperty("id", out var idProp)
                    ? idProp.GetString() ?? string.Empty
                    : string.Empty;

                var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (item.TryGetProperty("properties", out var props))
                {
                    foreach (var prop in props.EnumerateObject())
                    {
                        properties[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString() ?? string.Empty
                            : prop.Value.ToString();
                    }
                }

                records.Add(new HubSpotRecord(id, objectType, properties, DateTimeOffset.UtcNow));
            }
        }

        return records;
    }

    private async global::System.Threading.Tasks.Task<HttpResponseMessage> SendAuthorizedRequestAsync(
        HttpMethod method, string url, HttpContent? content, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _totalRequests);

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        if (content is not null)
        {
            request.Content = content;
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        UpdateRateLimitInfo(response);

        if (!response.IsSuccessStatusCode)
        {
            Interlocked.Increment(ref _failedRequests);
            Telemetry.HubSpotErrors.Add(1, new KeyValuePair<string, object?>("status_code", (int)response.StatusCode));

            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("HubSpot API returned {StatusCode}: {Body}", response.StatusCode, errorBody);
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

            // HubSpot returns Retry-After header on 429
            if (response.StatusCode == HttpStatusCode.TooManyRequests &&
                response.Headers.RetryAfter?.Delta is { } retryAfter)
            {
                delayMs = Math.Max(delayMs, (int)retryAfter.TotalMilliseconds);
            }

            _logger.LogWarning(
                "HubSpot request failed with {StatusCode}, retrying in {DelayMs}ms (attempt {Attempt}/{MaxRetries})",
                response.StatusCode, delayMs, attempt, _options.MaxRetries);

            Telemetry.HubSpotRetries.Add(1);

            await global::System.Threading.Tasks.Task.Delay(delayMs, cancellationToken);

            // Re-authenticate on 401
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await EnsureAuthenticatedAsync(cancellationToken);
            }
        }
    }

    private void UpdateRateLimitInfo(HttpResponseMessage response)
    {
        // HubSpot uses X-HubSpot-RateLimit-Daily-Remaining header
        if (response.Headers.TryGetValues("X-HubSpot-RateLimit-Daily-Remaining", out var values))
        {
            string? header = values.FirstOrDefault();
            if (header is not null && int.TryParse(header, out int remaining))
            {
                _dailyRateLimitRemaining = remaining;
                Telemetry.HubSpotRateLimitRemaining.Record(remaining);

                if (remaining < 1000)
                {
                    _logger.LogWarning("HubSpot daily rate limit approaching: {Remaining} remaining", remaining);
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

    private static string NormalizeObjectType(string objectType)
    {
        return objectType.ToLowerInvariant() switch
        {
            "contact" or "contacts" => "contacts",
            "deal" or "deals" => "deals",
            "company" or "companies" => "companies",
            "ticket" or "tickets" => "tickets",
            _ => objectType.ToLowerInvariant()
        };
    }
}

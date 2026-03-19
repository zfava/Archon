using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using ArchonAI.Common.Observability;
using ObsTelemetry = ArchonAI.Common.Observability.Telemetry;
using ArchonAI.Connectors.Framework;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;

namespace ArchonAI.Connectors.Salesforce;

public sealed class SalesforceConnector : ISalesforceConnector, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IEventBus _eventBus;
    private readonly ILogger<SalesforceConnector> _logger;
    private readonly SalesforceOptions _options;
    private readonly ConnectorResilienceRegistry? _resilienceRegistry;
    private readonly ConnectorShadowMetrics _shadowMetrics;

    private string? _accessToken;
    private string? _instanceUrl;
    private DateTimeOffset? _tokenExpiresAtUtc;
    private DateTimeOffset? _lastAuthenticatedAtUtc;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    private long _totalRequests;
    private long _failedRequests;
    private int _rateLimitRemaining = int.MaxValue;

    public SalesforceConnector(
        HttpClient httpClient,
        IEventBus eventBus,
        ILogger<SalesforceConnector> logger,
        IOptions<SalesforceOptions> options,
        ConnectorResilienceRegistry? resilienceRegistry = null,
        ConnectorShadowMetrics? shadowMetrics = null)
    {
        _httpClient = httpClient;
        _eventBus = eventBus;
        _logger = logger;
        _options = options.Value;
        _resilienceRegistry = resilienceRegistry;
        _shadowMetrics = shadowMetrics ?? new ConnectorShadowMetrics();
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds);
    }

    public string SystemName => "salesforce";

    public async global::System.Threading.Tasks.Task<SalesforceAuthResult> AuthenticateAsync(CancellationToken cancellationToken = default)
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

    public async global::System.Threading.Tasks.Task<IReadOnlyList<SalesforceRecord>> QueryAccountsAsync(
        string soqlFilter, CancellationToken cancellationToken = default)
    {
        string soql = string.IsNullOrWhiteSpace(soqlFilter)
            ? "SELECT Id, Name, Industry, Type FROM Account LIMIT 200"
            : $"SELECT Id, Name, Industry, Type FROM Account WHERE {soqlFilter} LIMIT 200";

        return await ExecuteQueryAsync("Account", soql, cancellationToken);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<SalesforceRecord>> QueryContactsAsync(
        string soqlFilter, CancellationToken cancellationToken = default)
    {
        string soql = string.IsNullOrWhiteSpace(soqlFilter)
            ? "SELECT Id, FirstName, LastName, Email, AccountId FROM Contact LIMIT 200"
            : $"SELECT Id, FirstName, LastName, Email, AccountId FROM Contact WHERE {soqlFilter} LIMIT 200";

        return await ExecuteQueryAsync("Contact", soql, cancellationToken);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<SalesforceRecord>> QueryOpportunitiesAsync(
        string soqlFilter, CancellationToken cancellationToken = default)
    {
        string soql = string.IsNullOrWhiteSpace(soqlFilter)
            ? "SELECT Id, Name, StageName, Amount, CloseDate, AccountId FROM Opportunity LIMIT 200"
            : $"SELECT Id, Name, StageName, Amount, CloseDate, AccountId FROM Opportunity WHERE {soqlFilter} LIMIT 200";

        return await ExecuteQueryAsync("Opportunity", soql, cancellationToken);
    }

    public async global::System.Threading.Tasks.Task<string> CreateRecordAsync(
        string objectType, IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken = default)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("Salesforce.CreateRecord");
        activity?.SetTag("sf.object_type", objectType);

        await EnsureAuthenticatedAsync(cancellationToken);

        string url = $"{_instanceUrl}/services/data/{_options.ApiVersion}/sobjects/{Uri.EscapeDataString(objectType)}";

        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Post, url, JsonContent.Create(fields), cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        string recordId = body.GetProperty("id").GetString() ?? string.Empty;

        _logger.LogInformation("Created Salesforce {ObjectType} record {RecordId}", objectType, recordId);
        ObsTelemetry.SalesforceWriteOps.Add(1, new KeyValuePair<string, object?>("operation", "create"), new KeyValuePair<string, object?>("object_type", objectType));

        await EmitAuditEventAsync("salesforce.record.created", objectType, recordId, cancellationToken);

        return recordId;
    }

    public async global::System.Threading.Tasks.Task<string> UpdateRecordAsync(
        string objectType, string recordId, IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken = default)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("Salesforce.UpdateRecord");
        activity?.SetTag("sf.object_type", objectType);
        activity?.SetTag("sf.record_id", recordId);

        await EnsureAuthenticatedAsync(cancellationToken);

        string url = $"{_instanceUrl}/services/data/{_options.ApiVersion}/sobjects/{Uri.EscapeDataString(objectType)}/{Uri.EscapeDataString(recordId)}";

        await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(new HttpMethod("PATCH"), url, JsonContent.Create(fields), cancellationToken),
            cancellationToken);

        _logger.LogInformation("Updated Salesforce {ObjectType} record {RecordId}", objectType, recordId);
        ObsTelemetry.SalesforceWriteOps.Add(1, new KeyValuePair<string, object?>("operation", "update"), new KeyValuePair<string, object?>("object_type", objectType));

        await EmitAuditEventAsync("salesforce.record.updated", objectType, recordId, cancellationToken);

        return recordId;
    }

    public SalesforceConnectorStatus GetStatus()
    {
        return new SalesforceConnectorStatus(
            IsConnected: _accessToken is not null && (_tokenExpiresAtUtc is null || _tokenExpiresAtUtc > DateTimeOffset.UtcNow),
            InstanceUrl: _instanceUrl,
            LastAuthenticatedAtUtc: _lastAuthenticatedAtUtc,
            TokenExpiresAtUtc: _tokenExpiresAtUtc,
            TotalRequests: Interlocked.Read(ref _totalRequests),
            FailedRequests: Interlocked.Read(ref _failedRequests),
            RateLimitRemaining: _rateLimitRemaining,
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task PushResultAsync(ExecutionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Salesforce connector received execution result for task {TaskId}", result.TaskId);

        await EmitAuditEventAsync("salesforce.result.pushed", "ExecutionResult", result.TaskId.ToString(), cancellationToken);
    }

    public void Dispose()
    {
        _authLock.Dispose();
    }

    // --- Private helpers ---

    private async global::System.Threading.Tasks.Task<SalesforceAuthResult> AuthenticateCoreAsync(CancellationToken cancellationToken)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("Salesforce.Authenticate");

        try
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["username"] = _options.Username,
                ["password"] = $"{_options.Password}{_options.SecurityToken}"
            });

            var response = await _httpClient.PostAsync($"{_options.LoginUrl}/services/oauth2/token", form, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string error = body.TryGetProperty("error_description", out var desc)
                    ? desc.GetString() ?? "Unknown error"
                    : "Authentication failed";

                _logger.LogError("Salesforce OAuth failed: {Error}", error);
                Interlocked.Increment(ref _failedRequests);
                return new SalesforceAuthResult(false, null, null, error);
            }

            _accessToken = body.GetProperty("access_token").GetString();
            _instanceUrl = body.GetProperty("instance_url").GetString();
            _lastAuthenticatedAtUtc = DateTimeOffset.UtcNow;
            _tokenExpiresAtUtc = _lastAuthenticatedAtUtc.Value.AddHours(2);

            _logger.LogInformation("Salesforce OAuth succeeded for instance {InstanceUrl}", _instanceUrl);
            ObsTelemetry.SalesforceAuthAttempts.Add(1, new KeyValuePair<string, object?>("result", "success"));

            return new SalesforceAuthResult(true, _instanceUrl, _tokenExpiresAtUtc, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Salesforce OAuth failed with exception");
            ObsTelemetry.SalesforceAuthAttempts.Add(1, new KeyValuePair<string, object?>("result", "failure"));
            Interlocked.Increment(ref _failedRequests);
            return new SalesforceAuthResult(false, null, null, ex.Message);
        }
    }

    private async global::System.Threading.Tasks.Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _tokenExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return;
        }

        var result = await AuthenticateAsync(cancellationToken);
        if (!result.IsAuthenticated)
        {
            throw new InvalidOperationException($"Salesforce authentication failed: {result.Error}");
        }
    }

    private async global::System.Threading.Tasks.Task<IReadOnlyList<SalesforceRecord>> ExecuteQueryAsync(
        string objectType, string soql, CancellationToken cancellationToken)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("Salesforce.Query");
        activity?.SetTag("sf.object_type", objectType);

        await EnsureAuthenticatedAsync(cancellationToken);

        string url = $"{_instanceUrl}/services/data/{_options.ApiVersion}/query?q={Uri.EscapeDataString(soql)}";

        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, url, null, cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var records = new List<SalesforceRecord>();

        if (body.TryGetProperty("records", out var recordsElement))
        {
            foreach (var record in recordsElement.EnumerateArray())
            {
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string id = string.Empty;

                foreach (var property in record.EnumerateObject())
                {
                    if (property.Name.Equals("attributes", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string value = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.ToString();

                    if (property.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))
                        id = value;

                    fields[property.Name] = value;
                }

                records.Add(new SalesforceRecord(id, objectType, fields, DateTimeOffset.UtcNow));
            }
        }

        _logger.LogInformation("Salesforce query returned {Count} {ObjectType} records", records.Count, objectType);
        ObsTelemetry.SalesforceQueryOps.Add(1, new KeyValuePair<string, object?>("object_type", objectType));

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

        var sw = Stopwatch.StartNew();
        var response = await _httpClient.SendAsync(request, cancellationToken);
        sw.Stop();
        UpdateRateLimitInfo(response);

        bool success = response.IsSuccessStatusCode;
        _shadowMetrics.RecordRequest(SystemName, success, sw.Elapsed.TotalMilliseconds);

        if (!success)
        {
            Interlocked.Increment(ref _failedRequests);
            ObsTelemetry.SalesforceErrors.Add(1, new KeyValuePair<string, object?>("status_code", (int)response.StatusCode));

            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Salesforce API returned {StatusCode}: {Body}", response.StatusCode, errorBody);
        }

        return response;
    }

    private async global::System.Threading.Tasks.Task<HttpResponseMessage> ExecuteWithRetryAsync(
        Func<global::System.Threading.Tasks.Task<HttpResponseMessage>> operation,
        CancellationToken cancellationToken)
    {
        // Wrap the retry loop with circuit breaker if available
        if (_resilienceRegistry is not null)
        {
            var pipeline = _resilienceRegistry.GetOrCreatePipeline(SystemName);
            try
            {
                return await pipeline.ExecuteAsync(async ct =>
                {
                    return await ExecuteWithRetryCoreAsync(operation, ct);
                }, cancellationToken);
            }
            catch (BrokenCircuitException ex)
            {
                _logger.LogWarning("Salesforce circuit breaker is open. Request rejected.");
                throw new ConnectorCircuitOpenException(SystemName, ex);
            }
        }

        return await ExecuteWithRetryCoreAsync(operation, cancellationToken);
    }

    private async global::System.Threading.Tasks.Task<HttpResponseMessage> ExecuteWithRetryCoreAsync(
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

            if (response.StatusCode == HttpStatusCode.TooManyRequests &&
                response.Headers.RetryAfter?.Delta is { } retryAfter)
            {
                delayMs = Math.Max(delayMs, (int)retryAfter.TotalMilliseconds);
            }

            _logger.LogWarning(
                "Salesforce request failed with {StatusCode}, retrying in {DelayMs}ms (attempt {Attempt}/{MaxRetries})",
                response.StatusCode, delayMs, attempt, _options.MaxRetries);

            ObsTelemetry.SalesforceRetries.Add(1);

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
        if (response.Headers.TryGetValues("Sforce-Limit-Info", out var values))
        {
            string? header = values.FirstOrDefault();
            if (header is not null)
            {
                // Format: "api-usage=25/15000"
                var parts = header.Split('=', '/');
                if (parts.Length >= 3 &&
                    int.TryParse(parts[1], out int used) &&
                    int.TryParse(parts[2], out int limit))
                {
                    _rateLimitRemaining = limit - used;
                    ObsTelemetry.SalesforceRateLimitRemaining.Record(_rateLimitRemaining);

                    if (_rateLimitRemaining < limit * _options.RateLimitBufferPercent / 100)
                    {
                        _logger.LogWarning(
                            "Salesforce API rate limit approaching: {Remaining}/{Limit} remaining",
                            _rateLimitRemaining, limit);
                    }
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

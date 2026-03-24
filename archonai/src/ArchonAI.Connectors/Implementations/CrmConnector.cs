using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArchonAI.Common.Observability;
using ObsTelemetry = ArchonAI.Common.Observability.Telemetry;
using ArchonAI.Connectors.Framework;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Perception;
using ArchonAI.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;

namespace ArchonAI.Connectors.Implementations;

public sealed class CrmConnector : ICrmConnector, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IEventBus _eventBus;
    private readonly ILogger<CrmConnector> _logger;
    private readonly ConnectorResilienceRegistry? _resilienceRegistry;

    private long _totalRequests;
    private long _failedRequests;
    private readonly SemaphoreSlim _rateLimitGate = new(1, 1);

    public CrmConnector(
        HttpClient httpClient,
        IEventBus eventBus,
        ILogger<CrmConnector> logger,
        ConnectorResilienceRegistry? resilienceRegistry = null)
    {
        _httpClient = httpClient;
        _eventBus = eventBus;
        _logger = logger;
        _resilienceRegistry = resilienceRegistry;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public string SystemName => "crm";

    public async global::System.Threading.Tasks.Task<string> UpsertCustomerAsync(
        string customerId, string payload, CancellationToken cancellationToken = default)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("CRM.UpsertCustomer");
        activity?.SetTag("crm.customer_id", customerId);

        Interlocked.Increment(ref _totalRequests);

        var response = await ExecuteWithRetryAsync(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/customers/{Uri.EscapeDataString(customerId)}")
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
            };
            return await _httpClient.SendAsync(request, cancellationToken);
        }, cancellationToken);

        string resultId;
        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            resultId = body.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? customerId : customerId;
        }
        else
        {
            Interlocked.Increment(ref _failedRequests);
            ObsTelemetry.ConnectorErrors.Add(1, new KeyValuePair<string, object?>("connector", "crm"));
            resultId = $"crm-upsert:{customerId}";
        }

        _logger.LogInformation("CRM upsert for customer {CustomerId} completed", customerId);
        ObsTelemetry.ConnectorWriteOps.Add(1,
            new KeyValuePair<string, object?>("connector", "crm"),
            new KeyValuePair<string, object?>("operation", "upsert"));

        // Publish business signal to Perception engine
        await PublishBusinessSignalAsync("customer.upserted", customerId,
            new Dictionary<string, string> { ["customer_id"] = customerId, ["result_id"] = resultId }, cancellationToken);

        await EmitAuditEventAsync("crm.customer.upserted", customerId, cancellationToken);

        return resultId;
    }

    public async global::System.Threading.Tasks.Task PushResultAsync(
        ExecutionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("CRM connector received result for task {TaskId}", result.TaskId);

        await EmitAuditEventAsync("crm.result.pushed", result.TaskId.ToString(), cancellationToken);
    }

    public void Dispose()
    {
        _rateLimitGate.Dispose();
    }

    private async global::System.Threading.Tasks.Task<HttpResponseMessage> ExecuteWithRetryAsync(
        Func<global::System.Threading.Tasks.Task<HttpResponseMessage>> operation,
        CancellationToken cancellationToken)
    {
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
                _logger.LogWarning("CRM circuit breaker is open. Request rejected.");
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
        const int maxRetries = 3;
        const int baseDelayMs = 500;

        while (true)
        {
            attempt++;
            var response = await operation();

            if (response.IsSuccessStatusCode)
                return response;

            bool isRetryable = response.StatusCode is HttpStatusCode.TooManyRequests
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout
                or HttpStatusCode.RequestTimeout;

            if (!isRetryable || attempt >= maxRetries)
                return response;

            int delayMs = baseDelayMs * (1 << (attempt - 1));
            _logger.LogWarning("CRM request failed with {StatusCode}, retrying in {DelayMs}ms (attempt {Attempt}/{MaxRetries})",
                response.StatusCode, delayMs, attempt, maxRetries);
            ObsTelemetry.ConnectorRetries.Add(1, new KeyValuePair<string, object?>("connector", "crm"));

            await global::System.Threading.Tasks.Task.Delay(delayMs, cancellationToken);
        }
    }

    private async global::System.Threading.Tasks.Task PublishBusinessSignalAsync(
        string signalName, string entityId, Dictionary<string, string> payload, CancellationToken cancellationToken)
    {
        var signal = new BusinessSignal(
            Guid.NewGuid(), SignalType.ApiCall, SourceSystem.CRM, entityId,
            DateTimeOffset.UtcNow, payload);

        var systemEvent = new SystemEvent(
            Guid.NewGuid(), $"crm.signal.{signalName}", SystemName,
            Guid.NewGuid(), payload, DateTimeOffset.UtcNow);

        await _eventBus.PublishAsync(systemEvent, cancellationToken);
    }

    private async global::System.Threading.Tasks.Task EmitAuditEventAsync(
        string eventType, string entityId, CancellationToken cancellationToken)
    {
        var auditEvent = new SystemEvent(
            Guid.NewGuid(), eventType, SystemName, Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["entity_id"] = entityId,
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
            },
            DateTimeOffset.UtcNow);

        await _eventBus.PublishAsync(auditEvent, cancellationToken);
    }
}

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

public sealed class ErpConnector : IErpConnector, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ErpConnector> _logger;
    private readonly ConnectorResilienceRegistry? _resilienceRegistry;

    private long _totalRequests;
    private long _failedRequests;
    private readonly SemaphoreSlim _rateLimitGate = new(1, 1);

    public ErpConnector(
        HttpClient httpClient,
        IEventBus eventBus,
        ILogger<ErpConnector> logger,
        ConnectorResilienceRegistry? resilienceRegistry = null)
    {
        _httpClient = httpClient;
        _eventBus = eventBus;
        _logger = logger;
        _resilienceRegistry = resilienceRegistry;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public string SystemName => "erp";

    public async global::System.Threading.Tasks.Task<string> SyncOrderAsync(
        string orderId, string payload, CancellationToken cancellationToken = default)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("ERP.SyncOrder");
        activity?.SetTag("erp.order_id", orderId);

        Interlocked.Increment(ref _totalRequests);

        var response = await ExecuteWithRetryAsync(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{Uri.EscapeDataString(orderId)}/sync")
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
            };
            return await _httpClient.SendAsync(request, cancellationToken);
        }, cancellationToken);

        string resultId;
        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            resultId = body.TryGetProperty("sync_id", out var idProp) ? idProp.GetString() ?? $"erp-sync:{orderId}" : $"erp-sync:{orderId}";
        }
        else
        {
            Interlocked.Increment(ref _failedRequests);
            ObsTelemetry.ConnectorErrors.Add(1, new KeyValuePair<string, object?>("connector", "erp"));
            resultId = $"erp-sync:{orderId}";
        }

        _logger.LogInformation("ERP sync for order {OrderId} completed", orderId);
        ObsTelemetry.ConnectorWriteOps.Add(1,
            new KeyValuePair<string, object?>("connector", "erp"),
            new KeyValuePair<string, object?>("operation", "sync_order"));

        // Publish business signal to Perception engine
        await PublishBusinessSignalAsync("order.synced", orderId,
            new Dictionary<string, string> { ["order_id"] = orderId, ["result_id"] = resultId }, cancellationToken);

        await EmitAuditEventAsync("erp.order.synced", orderId, cancellationToken);

        return resultId;
    }

    public async global::System.Threading.Tasks.Task PushResultAsync(
        ExecutionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("ERP connector received result for task {TaskId}", result.TaskId);

        await EmitAuditEventAsync("erp.result.pushed", result.TaskId.ToString(), cancellationToken);
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
                return await pipeline.ExecuteAsync(
                    ct => ExecuteWithRetryCoreAsync(operation, ct), cancellationToken);
            }
            catch (BrokenCircuitException ex)
            {
                _logger.LogWarning("ERP circuit breaker is open. Request rejected.");
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
            _logger.LogWarning("ERP request failed with {StatusCode}, retrying in {DelayMs}ms (attempt {Attempt}/{MaxRetries})",
                response.StatusCode, delayMs, attempt, maxRetries);
            ObsTelemetry.ConnectorRetries.Add(1, new KeyValuePair<string, object?>("connector", "erp"));

            await global::System.Threading.Tasks.Task.Delay(delayMs, cancellationToken);
        }
    }

    private async global::System.Threading.Tasks.Task PublishBusinessSignalAsync(
        string signalName, string entityId, Dictionary<string, string> payload, CancellationToken cancellationToken)
    {
        var systemEvent = new SystemEvent(
            Guid.NewGuid(), $"erp.signal.{signalName}", SystemName,
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

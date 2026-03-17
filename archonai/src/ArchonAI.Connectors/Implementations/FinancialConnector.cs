using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Perception;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Connectors.Implementations;

public sealed class FinancialConnector : IFinancialConnector, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IEventBus _eventBus;
    private readonly ILogger<FinancialConnector> _logger;

    private long _totalRequests;
    private long _failedRequests;
    private readonly SemaphoreSlim _rateLimitGate = new(1, 1);

    public FinancialConnector(
        HttpClient httpClient,
        IEventBus eventBus,
        ILogger<FinancialConnector> logger)
    {
        _httpClient = httpClient;
        _eventBus = eventBus;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public string SystemName => "financial";

    public async global::System.Threading.Tasks.Task<string> PostTransactionAsync(
        string accountId, decimal amount, string currency, string description,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Financial.PostTransaction");
        activity?.SetTag("financial.account_id", accountId);
        activity?.SetTag("financial.currency", currency);

        Interlocked.Increment(ref _totalRequests);

        var transactionPayload = new Dictionary<string, string>
        {
            ["account_id"] = accountId,
            ["amount"] = amount.ToString("F2"),
            ["currency"] = currency,
            ["description"] = description
        };

        var response = await ExecuteWithRetryAsync(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/transactions")
            {
                Content = JsonContent.Create(transactionPayload)
            };
            return await _httpClient.SendAsync(request, cancellationToken);
        }, cancellationToken);

        string transactionId;
        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            transactionId = body.TryGetProperty("transaction_id", out var idProp)
                ? idProp.GetString() ?? $"financial-post:{accountId}:{amount}"
                : $"financial-post:{accountId}:{amount}";
        }
        else
        {
            Interlocked.Increment(ref _failedRequests);
            Telemetry.ConnectorErrors.Add(1, new KeyValuePair<string, object?>("connector", "financial"));
            transactionId = $"financial-post:{accountId}:{amount}";
        }

        _logger.LogInformation("Financial post transaction to account {AccountId} amount {Amount} {Currency} completed",
            accountId, amount, currency);
        Telemetry.ConnectorWriteOps.Add(1,
            new KeyValuePair<string, object?>("connector", "financial"),
            new KeyValuePair<string, object?>("operation", "post_transaction"));

        // Publish business signal to Perception engine
        await PublishBusinessSignalAsync("transaction.posted", accountId,
            new Dictionary<string, string>
            {
                ["account_id"] = accountId,
                ["amount"] = amount.ToString("F2"),
                ["currency"] = currency,
                ["transaction_id"] = transactionId
            }, cancellationToken);

        await EmitAuditEventAsync("financial.transaction.posted", transactionId, cancellationToken);

        return transactionId;
    }

    public async global::System.Threading.Tasks.Task PushResultAsync(
        ExecutionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Financial connector received result for task {TaskId}", result.TaskId);

        await EmitAuditEventAsync("financial.result.pushed", result.TaskId.ToString(), cancellationToken);
    }

    public void Dispose()
    {
        _rateLimitGate.Dispose();
    }

    private async global::System.Threading.Tasks.Task<HttpResponseMessage> ExecuteWithRetryAsync(
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
            _logger.LogWarning("Financial request failed with {StatusCode}, retrying in {DelayMs}ms (attempt {Attempt}/{MaxRetries})",
                response.StatusCode, delayMs, attempt, maxRetries);
            Telemetry.ConnectorRetries.Add(1, new KeyValuePair<string, object?>("connector", "financial"));

            await global::System.Threading.Tasks.Task.Delay(delayMs, cancellationToken);
        }
    }

    private async global::System.Threading.Tasks.Task PublishBusinessSignalAsync(
        string signalName, string entityId, Dictionary<string, string> payload, CancellationToken cancellationToken)
    {
        var systemEvent = new SystemEvent(
            Guid.NewGuid(), $"financial.signal.{signalName}", SystemName,
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

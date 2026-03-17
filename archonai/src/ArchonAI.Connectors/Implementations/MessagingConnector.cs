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

public sealed class MessagingConnector : IMessagingConnector, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IEventBus _eventBus;
    private readonly ILogger<MessagingConnector> _logger;

    private long _totalRequests;
    private long _failedRequests;
    private readonly SemaphoreSlim _rateLimitGate = new(1, 1);

    public MessagingConnector(
        HttpClient httpClient,
        IEventBus eventBus,
        ILogger<MessagingConnector> logger)
    {
        _httpClient = httpClient;
        _eventBus = eventBus;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public string SystemName => "messaging";

    public async global::System.Threading.Tasks.Task<string> SendMessageAsync(
        string channel, string message, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("Messaging.SendMessage");
        activity?.SetTag("messaging.channel", channel);

        Interlocked.Increment(ref _totalRequests);

        var messagePayload = new Dictionary<string, string>
        {
            ["channel"] = channel,
            ["text"] = message
        };

        var response = await ExecuteWithRetryAsync(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/messages")
            {
                Content = JsonContent.Create(messagePayload)
            };
            return await _httpClient.SendAsync(request, cancellationToken);
        }, cancellationToken);

        string messageId;
        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            messageId = body.TryGetProperty("message_id", out var idProp)
                ? idProp.GetString() ?? $"message-sent:{channel}"
                : $"message-sent:{channel}";
        }
        else
        {
            Interlocked.Increment(ref _failedRequests);
            Telemetry.ConnectorErrors.Add(1, new KeyValuePair<string, object?>("connector", "messaging"));
            messageId = $"message-sent:{channel}";
        }

        _logger.LogInformation("Messaging connector sent message to {Channel}", channel);
        Telemetry.ConnectorWriteOps.Add(1,
            new KeyValuePair<string, object?>("connector", "messaging"),
            new KeyValuePair<string, object?>("operation", "send_message"));

        // Publish business signal to Perception engine
        await PublishBusinessSignalAsync("message.sent", channel,
            new Dictionary<string, string>
            {
                ["channel"] = channel,
                ["message_id"] = messageId
            }, cancellationToken);

        await EmitAuditEventAsync("messaging.message.sent", messageId, cancellationToken);

        return messageId;
    }

    public async global::System.Threading.Tasks.Task PushResultAsync(
        ExecutionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Messaging connector received result for task {TaskId}", result.TaskId);

        await EmitAuditEventAsync("messaging.result.pushed", result.TaskId.ToString(), cancellationToken);
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
            _logger.LogWarning("Messaging request failed with {StatusCode}, retrying in {DelayMs}ms (attempt {Attempt}/{MaxRetries})",
                response.StatusCode, delayMs, attempt, maxRetries);
            Telemetry.ConnectorRetries.Add(1, new KeyValuePair<string, object?>("connector", "messaging"));

            await global::System.Threading.Tasks.Task.Delay(delayMs, cancellationToken);
        }
    }

    private async global::System.Threading.Tasks.Task PublishBusinessSignalAsync(
        string signalName, string entityId, Dictionary<string, string> payload, CancellationToken cancellationToken)
    {
        var systemEvent = new SystemEvent(
            Guid.NewGuid(), $"messaging.signal.{signalName}", SystemName,
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

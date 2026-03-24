using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client;

namespace ArchonAI.Infrastructure.Eventing;

/// <summary>
/// NATS-backed event bus with handler retries and dead-letter publication.
/// </summary>
public sealed class NatsEventBus : IEventBus, IDisposable
{
    private readonly IConnection _connection;
    private readonly EventBusOptions _options;
    private readonly ILogger<NatsEventBus> _logger;
    private readonly ConcurrentDictionary<string, IAsyncSubscription> _subscriptions = new();

    public NatsEventBus(IOptions<EventBusOptions> options, ILogger<NatsEventBus> logger)
    {
        _options = options.Value;
        _logger = logger;

        var factory = new ConnectionFactory();
        _connection = factory.CreateConnection(_options.Url);
    }

    public global::System.Threading.Tasks.Task PublishAsync(SystemEvent systemEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string payload = JsonSerializer.Serialize(systemEvent);
        _connection.Publish(systemEvent.EventType, Encoding.UTF8.GetBytes(payload));
        _connection.Flush();
        ArchonAI.Common.Observability.Telemetry.EventsPublished.Add(1, new KeyValuePair<string, object?>("event.type", systemEvent.EventType));

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task SubscribeAsync(string eventType, Func<SystemEvent, CancellationToken, global::System.Threading.Tasks.Task> handler, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _subscriptions.GetOrAdd(eventType, _ =>
        {
            IAsyncSubscription subscription = _connection.SubscribeAsync(eventType);
            subscription.MessageHandler += (_, args) =>
            {
                _ = global::System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        string json = Encoding.UTF8.GetString(args.Message.Data);
                        SystemEvent? evt = JsonSerializer.Deserialize<SystemEvent>(json);
                        if (evt is null)
                        {
                            return;
                        }

                        await InvokeWithRetryAsync(evt, handler, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process event message for {EventType}", eventType);
                    }
                }, cancellationToken);
            };
            subscription.Start();
            return subscription;
        });

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    private async global::System.Threading.Tasks.Task InvokeWithRetryAsync(
        SystemEvent evt,
        Func<SystemEvent, CancellationToken, global::System.Threading.Tasks.Task> handler,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (int attempt = 0; attempt <= _options.HandlerRetryCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await handler(evt, cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < _options.HandlerRetryCount)
            {
                lastError = ex;
                int delayMs = _options.BaseRetryDelayMs * (attempt + 1);
                _logger.LogWarning(ex, "Event handler failed for {EventType}; retry {Attempt}/{RetryCount}", evt.EventType, attempt + 1, _options.HandlerRetryCount);
                await global::System.Threading.Tasks.Task.Delay(delayMs, cancellationToken);
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        var deadLetterEvent = evt with
        {
            Id = Guid.NewGuid(),
            EventType = evt.EventType + _options.DeadLetterSuffix,
            Payload = new Dictionary<string, string>(evt.Payload)
            {
                ["failureReason"] = lastError?.Message ?? "UnknownError",
                ["originalEventType"] = evt.EventType
            },
            OccurredAtUtc = DateTimeOffset.UtcNow
        };

        ArchonAI.Common.Observability.Telemetry.EventsDeadLettered.Add(1, new KeyValuePair<string, object?>("event.type", evt.EventType));
        await PublishAsync(deadLetterEvent, cancellationToken);
    }

    public void Dispose()
    {
        foreach (IAsyncSubscription sub in _subscriptions.Values)
        {
            sub.Unsubscribe();
            sub.Dispose();
        }

        _connection.Drain();
        _connection.Dispose();
    }
}

using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Wrap;

namespace ArchonAI.Infrastructure.Resilience;

/// <summary>
/// Wraps an IEventBus with circuit breaker and timeout policies.
/// When the circuit opens, publish calls fail fast and events are logged for later retry.
/// Subscribe calls are not wrapped — they are long-lived.
/// </summary>
public sealed class ResilientEventBus : IEventBus
{
    private readonly IEventBus _inner;
    private readonly AsyncPolicyWrap _pipeline;
    private readonly ILogger<ResilientEventBus> _logger;

    public ResilientEventBus(
        IEventBus inner,
        AsyncPolicyWrap pipeline,
        ILogger<ResilientEventBus> logger)
    {
        _inner = inner;
        _pipeline = pipeline;
        _logger = logger;
    }

    public async global::System.Threading.Tasks.Task PublishAsync(SystemEvent systemEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            await _pipeline.ExecuteAsync(
                ct => _inner.PublishAsync(systemEvent, ct),
                cancellationToken);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning(
                "Event bus circuit is open. Event {EventType} (id={EventId}) was dropped.",
                systemEvent.EventType, systemEvent.Id);
            // Event is lost — in production this would queue to a local buffer for retry
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Event bus publish failed. Event {EventType} (id={EventId}) was dropped.",
                systemEvent.EventType, systemEvent.Id);
        }
    }

    public global::System.Threading.Tasks.Task SubscribeAsync(
        string eventType,
        Func<SystemEvent, CancellationToken, global::System.Threading.Tasks.Task> handler,
        CancellationToken cancellationToken = default)
    {
        // Subscriptions are long-lived and managed by NATS — no circuit breaker wrapping
        return _inner.SubscribeAsync(eventType, handler, cancellationToken);
    }
}

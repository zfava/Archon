using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;

namespace ArchonAI.Infrastructure.Resilience;

/// <summary>
/// Publishes circuit breaker state transitions as structured log events and system events.
/// Tracks per-integration circuit state for Prometheus metrics.
/// </summary>
public sealed class CircuitBreakerStatePublisher
{
    private readonly IEventBus? _eventBus;
    private readonly ILogger<CircuitBreakerStatePublisher> _logger;

    // Current state per integration for metric scraping
    private readonly Dictionary<string, CircuitState> _states = new();
    private readonly object _lock = new();

    public CircuitBreakerStatePublisher(
        ILogger<CircuitBreakerStatePublisher> logger,
        IEventBus? eventBus = null)
    {
        _logger = logger;
        _eventBus = eventBus;
    }

    public CircuitState GetState(string integration)
    {
        lock (_lock)
        {
            return _states.TryGetValue(integration, out var state) ? state : CircuitState.Closed;
        }
    }

    public IReadOnlyDictionary<string, CircuitState> GetAllStates()
    {
        lock (_lock)
        {
            return new Dictionary<string, CircuitState>(_states);
        }
    }

    public void OnBreak(string integration, TimeSpan duration, Exception? exception)
    {
        SetState(integration, CircuitState.Open);

        _logger.LogWarning(
            "Circuit OPEN for {Integration}. Duration: {DurationSeconds}s. Reason: {Reason}",
            integration, duration.TotalSeconds, exception?.Message ?? "threshold exceeded");

        ArchonAI.Common.Observability.Telemetry.CircuitBreakerTransitions.Add(1,
            new KeyValuePair<string, object?>("integration", integration),
            new KeyValuePair<string, object?>("state", "open"));

        PublishTransitionEvent(integration, "open", exception?.Message);
    }

    public void OnHalfOpen(string integration)
    {
        SetState(integration, CircuitState.HalfOpen);

        _logger.LogInformation("Circuit HALF-OPEN for {Integration}. Testing recovery...", integration);

        ArchonAI.Common.Observability.Telemetry.CircuitBreakerTransitions.Add(1,
            new KeyValuePair<string, object?>("integration", integration),
            new KeyValuePair<string, object?>("state", "half_open"));

        PublishTransitionEvent(integration, "half_open", null);
    }

    public void OnReset(string integration)
    {
        SetState(integration, CircuitState.Closed);

        _logger.LogInformation("Circuit CLOSED for {Integration}. Integration recovered.", integration);

        ArchonAI.Common.Observability.Telemetry.CircuitBreakerTransitions.Add(1,
            new KeyValuePair<string, object?>("integration", integration),
            new KeyValuePair<string, object?>("state", "closed"));

        PublishTransitionEvent(integration, "closed", null);
    }

    private void SetState(string integration, CircuitState state)
    {
        lock (_lock)
        {
            _states[integration] = state;
        }

        // Update the gauge metric: 0=closed, 1=half-open, 2=open
        int stateValue = state switch
        {
            CircuitState.Closed => 0,
            CircuitState.HalfOpen => 1,
            CircuitState.Open => 2,
            _ => -1
        };
        ArchonAI.Common.Observability.Telemetry.CircuitBreakerState.Record(stateValue,
            new KeyValuePair<string, object?>("integration", integration));
    }

    private void PublishTransitionEvent(string integration, string newState, string? reason)
    {
        if (_eventBus is null) return;

        _ = global::System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var evt = new SystemEvent(
                    Id: Guid.NewGuid(),
                    EventType: "resilience.circuit_breaker.transition",
                    Source: "resilience",
                    CorrelationId: Guid.NewGuid(),
                    Payload: new Dictionary<string, string>
                    {
                        ["integration"] = integration,
                        ["state"] = newState,
                        ["reason"] = reason ?? string.Empty,
                        ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
                    },
                    OccurredAtUtc: DateTimeOffset.UtcNow);

                await _eventBus.PublishAsync(evt, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to publish circuit breaker event for {Integration}", integration);
            }
        });
    }
}

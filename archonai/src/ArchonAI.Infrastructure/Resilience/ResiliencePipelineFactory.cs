using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Bulkhead;
using Polly.Timeout;
using Polly.Wrap;
using PollyPolicy = Polly.Policy;

namespace ArchonAI.Infrastructure.Resilience;

/// <summary>
/// Creates composed Polly resilience pipelines (timeout -> bulkhead -> circuit breaker)
/// for connectors, model providers, and event bus integrations.
/// </summary>
public sealed class ResiliencePipelineFactory
{
    private readonly ResilienceOptions _options;
    private readonly CircuitBreakerStatePublisher _statePublisher;
    private readonly ILogger<ResiliencePipelineFactory> _logger;

    public ResiliencePipelineFactory(
        ResilienceOptions options,
        CircuitBreakerStatePublisher statePublisher,
        ILogger<ResiliencePipelineFactory> logger)
    {
        _options = options;
        _statePublisher = statePublisher;
        _logger = logger;
    }

    /// <summary>
    /// Creates a resilience pipeline for connector HTTP calls.
    /// Policy order (outermost first): Timeout -> Bulkhead -> CircuitBreaker
    /// </summary>
    public AsyncPolicyWrap<HttpResponseMessage> CreateConnectorHttpPipeline(string connectorName)
    {
        var opts = _options.Connectors;

        var timeout = PollyPolicy.TimeoutAsync<HttpResponseMessage>(
            TimeSpan.FromSeconds(opts.TimeoutSeconds),
            TimeoutStrategy.Optimistic,
            onTimeoutAsync: (_, ts, _) =>
            {
                _logger.LogWarning("Connector {Name} timed out after {Seconds}s", connectorName, ts.TotalSeconds);
                return global::System.Threading.Tasks.Task.CompletedTask;
            });

        var bulkhead = PollyPolicy.BulkheadAsync<HttpResponseMessage>(
            maxParallelization: opts.MaxConcurrentCalls,
            maxQueuingActions: opts.MaxQueueDepth,
            onBulkheadRejectedAsync: _ =>
            {
                _logger.LogWarning("Connector {Name} bulkhead rejected request (max={Max}, queue={Queue})",
                    connectorName, opts.MaxConcurrentCalls, opts.MaxQueueDepth);
                return global::System.Threading.Tasks.Task.CompletedTask;
            });

        var circuitBreaker = Polly.Policy<HttpResponseMessage>
            .Handle<Exception>()
            .OrResult(r => (int)r.StatusCode >= 500)
            .AdvancedCircuitBreakerAsync(
                failureThreshold: 0.5,
                samplingDuration: TimeSpan.FromSeconds(30),
                minimumThroughput: opts.CircuitBreakerThreshold,
                durationOfBreak: TimeSpan.FromSeconds(opts.CircuitBreakerDurationSeconds),
                onBreak: (outcome, duration) =>
                    _statePublisher.OnBreak(connectorName, duration, outcome.Exception),
                onReset: () => _statePublisher.OnReset(connectorName),
                onHalfOpen: () => _statePublisher.OnHalfOpen(connectorName));

        // Outermost -> innermost: timeout wraps bulkhead wraps circuit breaker
        return timeout.WrapAsync(bulkhead).WrapAsync(circuitBreaker);
    }

    /// <summary>
    /// Creates a resilience pipeline for model provider calls.
    /// </summary>
    public AsyncPolicyWrap CreateProviderPipeline(string providerName)
    {
        var opts = _options.Providers;

        var timeout = PollyPolicy.TimeoutAsync(
            TimeSpan.FromSeconds(opts.TimeoutSeconds),
            TimeoutStrategy.Optimistic,
            onTimeoutAsync: (_, ts, _) =>
            {
                _logger.LogWarning("Provider {Name} timed out after {Seconds}s", providerName, ts.TotalSeconds);
                return global::System.Threading.Tasks.Task.CompletedTask;
            });

        var bulkhead = PollyPolicy.BulkheadAsync(
            maxParallelization: opts.MaxConcurrentCalls,
            maxQueuingActions: opts.MaxQueueDepth,
            onBulkheadRejectedAsync: _ =>
            {
                _logger.LogWarning("Provider {Name} bulkhead rejected request", providerName);
                return global::System.Threading.Tasks.Task.CompletedTask;
            });

        var circuitBreaker = PollyPolicy
            .Handle<Exception>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: opts.CircuitBreakerThreshold,
                durationOfBreak: TimeSpan.FromSeconds(opts.CircuitBreakerDurationSeconds),
                onBreak: (ex, duration) =>
                    _statePublisher.OnBreak(providerName, duration, ex),
                onReset: () => _statePublisher.OnReset(providerName),
                onHalfOpen: () => _statePublisher.OnHalfOpen(providerName));

        return timeout.WrapAsync(bulkhead).WrapAsync(circuitBreaker);
    }

    /// <summary>
    /// Creates a resilience pipeline for event bus operations.
    /// </summary>
    public AsyncPolicyWrap CreateEventBusPipeline()
    {
        var opts = _options.EventBus;

        var timeout = PollyPolicy.TimeoutAsync(
            TimeSpan.FromSeconds(opts.TimeoutSeconds),
            TimeoutStrategy.Optimistic);

        var circuitBreaker = PollyPolicy
            .Handle<Exception>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: opts.CircuitBreakerThreshold,
                durationOfBreak: TimeSpan.FromSeconds(opts.CircuitBreakerDurationSeconds),
                onBreak: (ex, duration) =>
                    _statePublisher.OnBreak("event_bus", duration, ex),
                onReset: () => _statePublisher.OnReset("event_bus"),
                onHalfOpen: () => _statePublisher.OnHalfOpen("event_bus"));

        return timeout.WrapAsync(circuitBreaker);
    }
}

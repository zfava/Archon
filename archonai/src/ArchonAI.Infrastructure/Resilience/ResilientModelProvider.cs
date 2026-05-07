using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Models;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Wrap;

namespace ArchonAI.Infrastructure.Resilience;

/// <summary>
/// Wraps an IModelProvider with circuit breaker, bulkhead, and timeout policies.
/// When the circuit opens, the provider reports itself as unavailable so the
/// CompositeModelProvider can route to the next provider in the fallback chain.
/// </summary>
public sealed class ResilientModelProvider : IModelProvider
{
    private readonly IModelProvider _inner;
    private readonly AsyncPolicyWrap _pipeline;
    private readonly ILogger _logger;

    public ResilientModelProvider(
        IModelProvider inner,
        AsyncPolicyWrap pipeline,
        ILogger logger)
    {
        _inner = inner;
        _pipeline = pipeline;
        _logger = logger;
    }

    public string ProviderName => _inner.ProviderName;

    public bool CanHandle(string model)
    {
        try
        {
            // If circuit is open, this provider cannot handle requests
            return _inner.CanHandle(model);
        }
        catch
        {
            return false;
        }
    }

    public async Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _pipeline.ExecuteAsync(
                ct => _inner.GenerateAsync(request, ct),
                cancellationToken);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning("Provider {Provider} circuit is open. Request rejected.", ProviderName);
            throw new ProviderCircuitOpenException(ProviderName, ex);
        }
    }
}

/// <summary>
/// Thrown when a model provider's circuit breaker is open.
/// Signals the composite provider to try the next provider in the fallback chain.
/// </summary>
public sealed class ProviderCircuitOpenException : Exception
{
    public string Provider { get; }

    public ProviderCircuitOpenException(string provider, Exception? innerException = null)
        : base($"Circuit breaker is open for provider '{provider}'", innerException)
    {
        Provider = provider;
    }
}

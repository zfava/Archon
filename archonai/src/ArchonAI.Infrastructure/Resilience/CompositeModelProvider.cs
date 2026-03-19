using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Models;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Infrastructure.Resilience;

/// <summary>
/// Routes model requests through a chain of providers with automatic fallback.
/// When a provider's circuit opens, the next provider in the chain is tried.
/// If all providers are down, returns a clear error — never an echo stub.
/// </summary>
public sealed class CompositeModelProvider : IModelProvider
{
    private readonly IReadOnlyList<IModelProvider> _providers;
    private readonly ILogger<CompositeModelProvider> _logger;

    public CompositeModelProvider(
        IEnumerable<IModelProvider> providers,
        ILogger<CompositeModelProvider> logger)
    {
        _providers = providers.ToList();
        _logger = logger;
    }

    public string ProviderName => "composite";

    public bool CanHandle(string model)
    {
        return _providers.Any(p => p.CanHandle(model));
    }

    public async Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        var errors = new List<(string Provider, Exception Error)>();

        foreach (var provider in _providers)
        {
            if (!provider.CanHandle(request.Model ?? string.Empty))
                continue;

            try
            {
                var response = await provider.GenerateAsync(request, cancellationToken);
                return response;
            }
            catch (ProviderCircuitOpenException ex)
            {
                _logger.LogWarning("Provider {Provider} circuit open, trying next in chain", ex.Provider);
                errors.Add((ex.Provider, ex));
                ArchonAI.Common.Observability.Telemetry.ModelProviderFallbacks.Add(1,
                    new KeyValuePair<string, object?>("failed_provider", ex.Provider));
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Provider {Provider} failed, trying next in chain", provider.ProviderName);
                errors.Add((provider.ProviderName, ex));
                ArchonAI.Common.Observability.Telemetry.ModelProviderFallbacks.Add(1,
                    new KeyValuePair<string, object?>("failed_provider", provider.ProviderName));
                continue;
            }
        }

        // All providers exhausted
        string failedProviders = string.Join(", ", errors.Select(e => e.Provider));
        _logger.LogError(
            "All model providers failed for request. Failed providers: {Providers}. Model: {Model}",
            failedProviders, request.Model);

        ArchonAI.Common.Observability.Telemetry.ModelProviderAllCircuitsOpen.Add(1);

        throw new AllProvidersUnavailableException(
            $"All model providers are unavailable. Failed: [{failedProviders}]. " +
            $"This indicates a trust-tier downgrade condition. Model requested: {request.Model}",
            errors.LastOrDefault().Error);
    }
}

public sealed class AllProvidersUnavailableException : Exception
{
    public AllProvidersUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}

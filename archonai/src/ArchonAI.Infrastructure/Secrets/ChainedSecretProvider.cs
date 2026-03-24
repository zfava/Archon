using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Infrastructure.Secrets;

/// <summary>
/// Chains multiple secret providers with priority ordering.
/// First provider to return a non-null value wins.
/// Typical chain: FileSecretProvider → EnvironmentSecretProvider.
/// </summary>
public sealed class ChainedSecretProvider : ISecretProvider
{
    private readonly IReadOnlyList<ISecretProvider> _providers;
    private readonly ILogger<ChainedSecretProvider> _logger;

    public ChainedSecretProvider(IEnumerable<ISecretProvider> providers, ILogger<ChainedSecretProvider> logger)
    {
        _providers = providers.ToList();
        _logger = logger;
    }

    public bool SupportsRotation => _providers.Any(p => p.SupportsRotation);

    public string? GetSecret(string key)
    {
        foreach (var provider in _providers)
        {
            var value = provider.GetSecret(key);
            if (value is not null)
            {
                _logger.LogDebug("Secret resolved: key={SecretKey}, provider={ProviderType}", key, provider.GetType().Name);
                return value;
            }
        }
        _logger.LogDebug("Secret not found in any provider: key={SecretKey}", key);
        return null;
    }

    public string GetRequiredSecret(string key)
    {
        var value = GetSecret(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required secret '{key}' not found in any configured provider.");
        }
        return value;
    }
}

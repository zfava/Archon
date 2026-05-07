using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Infrastructure.Secrets;

/// <summary>
/// Default secret provider that reads from environment variables.
/// Used in Kubernetes (envFrom secretRef) and docker-compose (.env file).
/// Does not support rotation — pods must restart for new env values.
/// </summary>
public sealed class EnvironmentSecretProvider : ISecretProvider
{
    private readonly ILogger<EnvironmentSecretProvider> _logger;

    public EnvironmentSecretProvider(ILogger<EnvironmentSecretProvider> logger)
    {
        _logger = logger;
    }

    public bool SupportsRotation => false;

    public string? GetSecret(string key)
    {
        _logger.LogDebug("Secret access: key={SecretKey}, provider=environment", key);
        return Environment.GetEnvironmentVariable(key);
    }

    public string GetRequiredSecret(string key)
    {
        var value = GetSecret(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            _logger.LogError("Required secret not found: key={SecretKey}, provider=environment", key);
            throw new InvalidOperationException(
                $"Required secret '{key}' is not configured. Set it as an environment variable via Kubernetes Secret or .env file.");
        }
        return value;
    }
}

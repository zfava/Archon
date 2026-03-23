namespace ArchonAI.Core.Interfaces;

/// <summary>
/// Provides runtime access to secrets with auditable access logging.
/// Implementations must never log secret values — only access metadata.
/// </summary>
public interface ISecretProvider
{
    /// <summary>
    /// Retrieves a secret value by key. Returns null if not found.
    /// Every call is audit-logged with the caller and key (never the value).
    /// </summary>
    string? GetSecret(string key);

    /// <summary>
    /// Retrieves a secret, throwing if not found or empty.
    /// </summary>
    string GetRequiredSecret(string key);

    /// <summary>
    /// Returns true if the provider supports runtime rotation detection.
    /// </summary>
    bool SupportsRotation { get; }

    /// <summary>
    /// Async version of <see cref="GetSecret"/>. Default implementation delegates to the sync method.
    /// Providers with native async support (Vault, AWS, Azure) override this for true async I/O.
    /// </summary>
    Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
        => Task.FromResult(GetSecret(key));
}

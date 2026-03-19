using ArchonAI.Core.Models.Identity;

namespace ArchonAI.Core.Interfaces;

/// <summary>
/// Server-side store for OIDC login session bindings. Each entry is created
/// at /login time and consumed (deleted) at /callback time to prevent replay.
/// </summary>
public interface IOidcLoginSessionStore
{
    /// <summary>Store a new login session keyed by its state value.</summary>
    Task CreateAsync(OidcLoginSession session, CancellationToken ct = default);

    /// <summary>
    /// Atomically consume (retrieve and delete) the session for the given state.
    /// Returns null if the state does not exist (missing, already consumed, or never issued).
    /// </summary>
    Task<OidcLoginSession?> ConsumeAsync(string state, CancellationToken ct = default);
}

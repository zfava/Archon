using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;

namespace ArchonAI.Identity.Stores;

/// <summary>
/// In-memory implementation of the OIDC login session store.
/// Entries are single-use (consumed on callback) and expire automatically.
/// </summary>
public sealed class InMemoryOidcLoginSessionStore : IOidcLoginSessionStore
{
    private readonly ConcurrentDictionary<string, OidcLoginSession> _sessions = new();

    public Task CreateAsync(OidcLoginSession session, CancellationToken ct = default)
    {
        if (!_sessions.TryAdd(session.State, session))
            throw new InvalidOperationException("Duplicate OIDC login session state.");
        return Task.CompletedTask;
    }

    public Task<OidcLoginSession?> ConsumeAsync(string state, CancellationToken ct = default)
    {
        // Atomic remove — the session can only be consumed once (replay prevention).
        _sessions.TryRemove(state, out var session);
        return Task.FromResult(session);
    }
}

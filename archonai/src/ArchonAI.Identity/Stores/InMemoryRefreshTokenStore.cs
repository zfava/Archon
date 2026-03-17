using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;

namespace ArchonAI.Identity.Stores;

public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly ConcurrentDictionary<Guid, RefreshToken> _tokens = new();

    public Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken ct = default)
    {
        _tokens[token.Id] = token;
        return Task.FromResult(token);
    }

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var token = _tokens.Values.FirstOrDefault(t =>
            t.TokenHash == tokenHash && !t.IsRevoked && t.ExpiresAtUtc > DateTimeOffset.UtcNow);
        return Task.FromResult(token);
    }

    public Task RevokeAsync(Guid tokenId, CancellationToken ct = default)
    {
        if (_tokens.TryGetValue(tokenId, out var existing))
            _tokens[tokenId] = existing with { IsRevoked = true };
        return Task.CompletedTask;
    }

    public Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        foreach (var kvp in _tokens.Where(t => t.Value.UserId == userId && !t.Value.IsRevoked))
            _tokens[kvp.Key] = kvp.Value with { IsRevoked = true };
        return Task.CompletedTask;
    }
}

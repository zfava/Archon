using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;

namespace ArchonAI.Identity.Stores;

public sealed class InMemoryInviteTokenStore : IInviteTokenStore
{
    private readonly ConcurrentDictionary<Guid, InviteToken> _invites = new();

    public Task<InviteToken> CreateAsync(InviteToken invite, CancellationToken ct = default)
    {
        _invites[invite.Id] = invite;
        return Task.FromResult(invite);
    }

    public Task<InviteToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var invite = _invites.Values.FirstOrDefault(i =>
            i.TokenHash == tokenHash && !i.IsAccepted && i.ExpiresAtUtc > DateTimeOffset.UtcNow);
        return Task.FromResult(invite);
    }

    public Task AcceptAsync(Guid inviteId, CancellationToken ct = default)
    {
        if (_invites.TryGetValue(inviteId, out var existing))
            _invites[inviteId] = existing with { IsAccepted = true };
        return Task.CompletedTask;
    }
}

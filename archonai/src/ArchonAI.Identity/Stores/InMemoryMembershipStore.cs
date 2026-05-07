using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;

namespace ArchonAI.Identity.Stores;

public sealed class InMemoryMembershipStore : IMembershipStore
{
    private readonly ConcurrentDictionary<(Guid UserId, Guid OrgId), Membership> _memberships = new();

    public Task<Membership> CreateAsync(Membership membership, CancellationToken ct = default)
    {
        var key = (membership.UserId, membership.OrganizationId);
        if (!_memberships.TryAdd(key, membership))
            throw new InvalidOperationException("Membership already exists.");
        return Task.FromResult(membership);
    }

    public Task<Membership?> GetAsync(Guid userId, Guid orgId, CancellationToken ct = default)
    {
        _memberships.TryGetValue((userId, orgId), out var m);
        return Task.FromResult(m);
    }

    public Task<IReadOnlyList<Membership>> ListByUserAsync(Guid userId, CancellationToken ct = default)
    {
        IReadOnlyList<Membership> result = _memberships.Values
            .Where(m => m.UserId == userId)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Membership>> ListByOrganizationAsync(Guid orgId, CancellationToken ct = default)
    {
        IReadOnlyList<Membership> result = _memberships.Values
            .Where(m => m.OrganizationId == orgId)
            .ToList();
        return Task.FromResult(result);
    }

    public Task RemoveAsync(Guid userId, Guid orgId, CancellationToken ct = default)
    {
        _memberships.TryRemove((userId, orgId), out _);
        return Task.CompletedTask;
    }
}

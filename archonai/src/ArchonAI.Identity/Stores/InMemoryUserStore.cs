using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;

namespace ArchonAI.Identity.Stores;

public sealed class InMemoryUserStore : IUserStore
{
    private readonly ConcurrentDictionary<Guid, UserIdentity> _users = new();

    public Task<UserIdentity?> GetByIdAsync(Guid userId, CancellationToken ct = default)
    {
        _users.TryGetValue(userId, out var user);
        return Task.FromResult(user);
    }

    public Task<UserIdentity?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        var user = _users.Values.FirstOrDefault(u =>
            u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(user);
    }

    public Task<UserIdentity> CreateAsync(UserIdentity user, CancellationToken ct = default)
    {
        if (!_users.TryAdd(user.Id, user))
            throw new InvalidOperationException($"User {user.Id} already exists.");
        return Task.FromResult(user);
    }

    public Task<UserIdentity> UpdateAsync(UserIdentity user, CancellationToken ct = default)
    {
        _users[user.Id] = user;
        return Task.FromResult(user);
    }

    public Task<IReadOnlyList<UserIdentity>> ListByOrganizationAsync(Guid orgId, CancellationToken ct = default)
    {
        IReadOnlyList<UserIdentity> result = _users.Values
            .Where(u => u.OrganizationId == orgId)
            .ToList();
        return Task.FromResult(result);
    }
}

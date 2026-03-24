using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;

namespace ArchonAI.Identity.Stores;

public sealed class InMemoryOrganizationStore : IOrganizationStore
{
    private readonly ConcurrentDictionary<Guid, Organization> _orgs = new();

    public Task<Organization?> GetByIdAsync(Guid orgId, CancellationToken ct = default)
    {
        _orgs.TryGetValue(orgId, out var org);
        return Task.FromResult(org);
    }

    public Task<Organization?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var org = _orgs.Values.FirstOrDefault(o =>
            o.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(org);
    }

    public Task<Organization> CreateAsync(Organization org, CancellationToken ct = default)
    {
        if (!_orgs.TryAdd(org.Id, org))
            throw new InvalidOperationException($"Organization {org.Id} already exists.");
        return Task.FromResult(org);
    }

    public Task<IReadOnlyList<Organization>> ListAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Organization> result = _orgs.Values.ToList();
        return Task.FromResult(result);
    }
}

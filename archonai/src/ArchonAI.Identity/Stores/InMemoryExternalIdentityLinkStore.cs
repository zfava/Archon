using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;

namespace ArchonAI.Identity.Stores;

public sealed class InMemoryExternalIdentityLinkStore : IExternalIdentityLinkStore
{
    private readonly ConcurrentDictionary<Guid, ExternalIdentityLink> _links = new();

    public Task<ExternalIdentityLink?> GetByExternalSubjectAsync(
        string externalSubject, string externalIssuer, CancellationToken ct = default)
    {
        var link = _links.Values.FirstOrDefault(l =>
            l.ExternalSubject == externalSubject &&
            l.ExternalIssuer.Equals(externalIssuer, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(link);
    }

    public Task<ExternalIdentityLink?> GetByUserIdAsync(Guid userId, Guid orgId, CancellationToken ct = default)
    {
        var link = _links.Values.FirstOrDefault(l =>
            l.UserId == userId && l.OrganizationId == orgId);
        return Task.FromResult(link);
    }

    public Task<ExternalIdentityLink> CreateAsync(ExternalIdentityLink link, CancellationToken ct = default)
    {
        if (!_links.TryAdd(link.Id, link))
            throw new InvalidOperationException($"ExternalIdentityLink {link.Id} already exists.");
        return Task.FromResult(link);
    }

    public Task<ExternalIdentityLink> UpdateAsync(ExternalIdentityLink link, CancellationToken ct = default)
    {
        _links[link.Id] = link;
        return Task.FromResult(link);
    }

    public Task<IReadOnlyList<ExternalIdentityLink>> ListByOrganizationAsync(
        Guid orgId, CancellationToken ct = default)
    {
        IReadOnlyList<ExternalIdentityLink> result = _links.Values
            .Where(l => l.OrganizationId == orgId).ToList();
        return Task.FromResult(result);
    }
}

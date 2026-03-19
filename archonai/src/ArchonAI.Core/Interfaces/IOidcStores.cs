using ArchonAI.Core.Models.Identity;

namespace ArchonAI.Core.Interfaces;

public interface ITenantAuthConfigStore
{
    Task<TenantAuthConfig?> GetByOrganizationIdAsync(Guid orgId, CancellationToken ct = default);
    Task<TenantAuthConfig?> GetByIdAsync(Guid configId, CancellationToken ct = default);
    Task<IReadOnlyList<TenantAuthConfig>> ListAsync(CancellationToken ct = default);
    Task<TenantAuthConfig> CreateAsync(TenantAuthConfig config, CancellationToken ct = default);
    Task<TenantAuthConfig> UpdateAsync(TenantAuthConfig config, CancellationToken ct = default);
    Task DeleteAsync(Guid configId, CancellationToken ct = default);
}

public interface IExternalIdentityLinkStore
{
    Task<ExternalIdentityLink?> GetByExternalSubjectAsync(
        string externalSubject, string externalIssuer, CancellationToken ct = default);
    Task<ExternalIdentityLink?> GetByUserIdAsync(Guid userId, Guid orgId, CancellationToken ct = default);
    Task<ExternalIdentityLink> CreateAsync(ExternalIdentityLink link, CancellationToken ct = default);
    Task<ExternalIdentityLink> UpdateAsync(ExternalIdentityLink link, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalIdentityLink>> ListByOrganizationAsync(Guid orgId, CancellationToken ct = default);
}

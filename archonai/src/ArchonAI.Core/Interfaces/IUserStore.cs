using ArchonAI.Core.Models.Identity;

namespace ArchonAI.Core.Interfaces;

public interface IUserStore
{
    Task<UserIdentity?> GetByIdAsync(Guid userId, CancellationToken ct = default);
    Task<UserIdentity?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<UserIdentity> CreateAsync(UserIdentity user, CancellationToken ct = default);
    Task<UserIdentity> UpdateAsync(UserIdentity user, CancellationToken ct = default);
    Task<IReadOnlyList<UserIdentity>> ListByOrganizationAsync(Guid orgId, CancellationToken ct = default);
}

public interface IOrganizationStore
{
    Task<Organization?> GetByIdAsync(Guid orgId, CancellationToken ct = default);
    Task<Organization?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<Organization> CreateAsync(Organization org, CancellationToken ct = default);
    Task<IReadOnlyList<Organization>> ListAsync(CancellationToken ct = default);
}

public interface IMembershipStore
{
    Task<Membership> CreateAsync(Membership membership, CancellationToken ct = default);
    Task<Membership?> GetAsync(Guid userId, Guid orgId, CancellationToken ct = default);
    Task<IReadOnlyList<Membership>> ListByUserAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Membership>> ListByOrganizationAsync(Guid orgId, CancellationToken ct = default);
    Task RemoveAsync(Guid userId, Guid orgId, CancellationToken ct = default);
}

public interface IRefreshTokenStore
{
    Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken ct = default);
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task RevokeAsync(Guid tokenId, CancellationToken ct = default);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);
}

public interface IInviteTokenStore
{
    Task<InviteToken> CreateAsync(InviteToken invite, CancellationToken ct = default);
    Task<InviteToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task AcceptAsync(Guid inviteId, CancellationToken ct = default);
}

namespace ArchonAI.Core.Models.Identity;

/// <summary>
/// Maps a tenant (organization) to its external OIDC identity provider configuration.
/// Each tenant can have one active OIDC provider; the platform supports multiple tenants
/// using different providers simultaneously.
/// </summary>
public sealed record TenantAuthConfig(
    Guid Id,
    Guid OrganizationId,
    OidcProviderType ProviderType,
    string Authority,
    string ClientId,
    string ClientSecret,
    string? Domain,
    string[] Scopes,
    bool AutoProvision,
    string DefaultRole,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

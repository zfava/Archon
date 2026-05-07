namespace ArchonAI.Core.Models.Identity;

/// <summary>
/// Links an external OIDC identity (subject claim from the external IdP) to an internal
/// ArchonAI user. Enables JIT-provisioned users to re-authenticate via their corporate IdP.
/// External tokens are NEVER stored — only the subject identifier for matching.
/// </summary>
public sealed record ExternalIdentityLink(
    Guid Id,
    Guid UserId,
    Guid OrganizationId,
    OidcProviderType ProviderType,
    string ExternalSubject,
    string ExternalIssuer,
    string? ExternalEmail,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastUsedAtUtc);

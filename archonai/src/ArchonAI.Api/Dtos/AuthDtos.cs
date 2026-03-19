namespace ArchonAI.Api.Dtos;

public sealed record RegisterRequest(
    string OrganizationName,
    string Email,
    string Password,
    string DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record InviteRequest(string Email, string? Role);

public sealed record AcceptInviteRequest(
    string InviteToken,
    string Password,
    string? DisplayName);

public sealed record UserInfo(Guid Id, string Email, string DisplayName, string Role);
public sealed record OrgInfo(Guid Id, string Name, string Slug);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    UserInfo User,
    OrgInfo Organization);

public sealed record MeResponse(
    UserInfo User,
    OrgInfo Organization);

// MFA DTOs
public sealed record MfaLoginResponse(
    bool MfaRequired,
    string MfaToken,
    string[] Methods,
    UserInfo User);

public sealed record MfaChallengeRequest(
    string MfaToken,
    string Method,
    string? Code);

public sealed record TotpSetupResponse(
    string OtpAuthUri,
    string[] RecoveryCodes);

public sealed record TotpVerifyRequest(string Code);

public sealed record WebAuthnRegisterCompleteRequest(
    string CredentialId,
    string PublicKey,
    uint SignCount,
    string? DisplayName);

public sealed record MfaPolicyRequest(string Mode);
public sealed record MfaPolicyResponse(string Mode);

// OIDC DTOs
/// <summary>
/// Response from /login. Only the authorize URL, state (for redirect correlation),
/// and organization ID are returned. The nonce and code_verifier are held server-side
/// in the OIDC login session and never exposed to the client.
/// </summary>
public sealed record OidcLoginResponse(
    string AuthorizeUrl,
    string State,
    Guid OrganizationId);

/// <summary>
/// OIDC callback request. The client sends the authorization code received from the IdP
/// redirect and the state for server-side validation. The code_verifier is held server-side
/// and injected into the back-channel token exchange automatically.
/// </summary>
public sealed record OidcCallbackRequest(
    string Code,
    string State,
    Guid OrganizationId);

public sealed record OidcCallbackResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    UserInfo User,
    OrgInfo Organization,
    bool IsNewUser);

public sealed record TenantAuthConfigResponse(
    Guid Id,
    Guid OrganizationId,
    string ProviderType,
    string Authority,
    string ClientId,
    string? Domain,
    string[] Scopes,
    bool AutoProvision,
    string DefaultRole,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record CreateTenantAuthConfigRequest(
    Guid OrganizationId,
    string ProviderType,
    string Authority,
    string ClientId,
    string ClientSecret,
    string? Domain,
    string[]? Scopes,
    bool AutoProvision,
    string? DefaultRole,
    bool IsEnabled);

public sealed record UpdateTenantAuthConfigRequest(
    string? ProviderType,
    string? Authority,
    string? ClientId,
    string? ClientSecret,
    string? Domain,
    string[]? Scopes,
    bool? AutoProvision,
    string? DefaultRole,
    bool? IsEnabled);

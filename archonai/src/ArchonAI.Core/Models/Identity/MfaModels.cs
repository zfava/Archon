namespace ArchonAI.Core.Models.Identity;

/// <summary>TOTP credential for a user. Secret is encrypted at rest.</summary>
public sealed record TotpCredential(
    Guid Id,
    Guid UserId,
    string EncryptedSecret,
    bool IsVerified,
    DateTimeOffset CreatedAtUtc);

/// <summary>WebAuthn/FIDO2 credential for a user. Supports multiple per user.</summary>
public sealed record WebAuthnCredential(
    Guid Id,
    Guid UserId,
    byte[] CredentialId,
    byte[] PublicKey,
    uint SignCount,
    string DisplayName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastUsedAtUtc);

/// <summary>Single-use bcrypt-hashed recovery code.</summary>
public sealed record MfaRecoveryCode(
    Guid Id,
    Guid UserId,
    string CodeHash,
    bool IsUsed,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UsedAtUtc);

/// <summary>Short-lived MFA challenge token issued after password verification.</summary>
public sealed record MfaChallenge(
    Guid Id,
    Guid UserId,
    string TokenHash,
    string[] AllowedMethods,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc,
    bool IsUsed);

/// <summary>Tenant-level MFA policy configuration.</summary>
public sealed record MfaPolicy(
    Guid OrganizationId,
    MfaPolicyMode Mode,
    DateTimeOffset UpdatedAtUtc);

public enum MfaPolicyMode
{
    Disabled,
    Optional,
    Required
}

/// <summary>Summary of a user's MFA enrollment status.</summary>
public sealed record MfaStatus(
    bool TotpEnabled,
    bool WebAuthnEnabled,
    int WebAuthnCredentialCount,
    int RecoveryCodesRemaining,
    string[] EnabledMethods);

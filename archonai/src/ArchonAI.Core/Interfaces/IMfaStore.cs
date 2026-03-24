using ArchonAI.Core.Models.Identity;

namespace ArchonAI.Core.Interfaces;

/// <summary>
/// Storage interface for all MFA-related entities.
/// </summary>
public interface IMfaStore
{
    // ── TOTP ────────────────────────────────────────────────────
    Task<TotpCredential?> GetTotpCredentialAsync(Guid userId, CancellationToken ct = default);
    Task CreateTotpCredentialAsync(TotpCredential credential, CancellationToken ct = default);
    Task UpdateTotpCredentialAsync(TotpCredential credential, CancellationToken ct = default);
    Task DeleteTotpCredentialAsync(Guid userId, CancellationToken ct = default);

    // ── WebAuthn ────────────────────────────────────────────────
    Task<IReadOnlyList<WebAuthnCredential>> GetWebAuthnCredentialsAsync(Guid userId, CancellationToken ct = default);
    Task<WebAuthnCredential?> GetWebAuthnCredentialByIdAsync(byte[] credentialId, CancellationToken ct = default);
    Task CreateWebAuthnCredentialAsync(WebAuthnCredential credential, CancellationToken ct = default);
    Task UpdateWebAuthnCredentialAsync(WebAuthnCredential credential, CancellationToken ct = default);
    Task DeleteWebAuthnCredentialAsync(Guid credentialId, CancellationToken ct = default);

    // ── Recovery codes ──────────────────────────────────────────
    Task<IReadOnlyList<MfaRecoveryCode>> GetRecoveryCodesAsync(Guid userId, CancellationToken ct = default);
    Task CreateRecoveryCodesAsync(IEnumerable<MfaRecoveryCode> codes, CancellationToken ct = default);
    Task MarkRecoveryCodeUsedAsync(Guid codeId, CancellationToken ct = default);
    Task DeleteAllRecoveryCodesAsync(Guid userId, CancellationToken ct = default);

    // ── MFA challenges ──────────────────────────────────────────
    Task CreateMfaChallengeAsync(MfaChallenge challenge, CancellationToken ct = default);
    Task<MfaChallenge?> GetMfaChallengeByHashAsync(string tokenHash, CancellationToken ct = default);
    Task MarkMfaChallengeUsedAsync(Guid challengeId, CancellationToken ct = default);

    // ── MFA policy ──────────────────────────────────────────────
    Task<MfaPolicy?> GetMfaPolicyAsync(Guid organizationId, CancellationToken ct = default);
    Task UpsertMfaPolicyAsync(MfaPolicy policy, CancellationToken ct = default);
}

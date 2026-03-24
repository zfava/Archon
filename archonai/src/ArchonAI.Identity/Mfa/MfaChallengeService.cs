using System.Security.Cryptography;
using System.Text;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Identity.Mfa;

/// <summary>
/// Manages MFA challenge tokens issued after password verification.
/// The challenge token has a 5-minute TTL and is single-use.
/// </summary>
public sealed class MfaChallengeService
{
    private readonly IMfaStore _store;
    private readonly TotpService _totp;
    private readonly WebAuthnService _webAuthn;
    private readonly ILogger<MfaChallengeService> _logger;
    private static readonly TimeSpan ChallengeTtl = TimeSpan.FromMinutes(5);

    public MfaChallengeService(
        IMfaStore store, TotpService totp, WebAuthnService webAuthn,
        ILogger<MfaChallengeService> logger)
    {
        _store = store;
        _totp = totp;
        _webAuthn = webAuthn;
        _logger = logger;
    }

    /// <summary>
    /// Creates an MFA challenge token for the user after password verification succeeds.
    /// Returns the raw token and the list of available MFA methods.
    /// </summary>
    public async Task<(string MfaToken, string[] Methods)?> CreateChallengeAsync(
        Guid userId, CancellationToken ct = default)
    {
        var methods = new List<string>();

        if (await _totp.IsEnabledAsync(userId, ct))
            methods.Add("totp");
        if (await _webAuthn.IsEnabledAsync(userId, ct))
            methods.Add("webauthn");

        // Recovery is always available if any MFA method is enabled
        var recoveryCodes = await _store.GetRecoveryCodesAsync(userId, ct);
        if (recoveryCodes.Any(c => !c.IsUsed))
            methods.Add("recovery");

        if (methods.Count == 0)
            return null; // No MFA enrolled

        string rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        string tokenHash = HashToken(rawToken);

        var challenge = new MfaChallenge(
            Id: Guid.NewGuid(),
            UserId: userId,
            TokenHash: tokenHash,
            AllowedMethods: methods.ToArray(),
            ExpiresAtUtc: DateTimeOffset.UtcNow + ChallengeTtl,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            IsUsed: false);

        await _store.CreateMfaChallengeAsync(challenge, ct);
        _logger.LogInformation("MFA challenge created for user {UserId}, methods: {Methods}",
            userId, string.Join(",", methods));

        return (rawToken, methods.ToArray());
    }

    /// <summary>
    /// Validates an MFA challenge token. Returns the associated user ID if valid.
    /// Marks the challenge as used (single-use).
    /// </summary>
    public async Task<Guid?> ValidateChallengeAsync(string mfaToken, CancellationToken ct = default)
    {
        string tokenHash = HashToken(mfaToken);
        var challenge = await _store.GetMfaChallengeByHashAsync(tokenHash, ct);

        if (challenge is null || challenge.IsUsed || challenge.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("MFA challenge validation failed: token invalid or expired");
            return null;
        }

        await _store.MarkMfaChallengeUsedAsync(challenge.Id, ct);
        return challenge.UserId;
    }

    /// <summary>
    /// Verifies a recovery code for MFA bypass. Single-use: the code is consumed.
    /// </summary>
    public async Task<bool> VerifyRecoveryCodeAsync(
        Guid userId, string code, CancellationToken ct = default)
    {
        var recoveryCodes = await _store.GetRecoveryCodesAsync(userId, ct);
        foreach (var rc in recoveryCodes.Where(c => !c.IsUsed))
        {
            if (TotpService.VerifyRecoveryCode(code, rc.CodeHash))
            {
                await _store.MarkRecoveryCodeUsedAsync(rc.Id, ct);
                _logger.LogInformation("Recovery code used for user {UserId}", userId);
                return true;
            }
        }
        _logger.LogWarning("Recovery code verification failed for user {UserId}", userId);
        return false;
    }

    /// <summary>
    /// Gets MFA status for a user.
    /// </summary>
    public async Task<MfaStatus> GetMfaStatusAsync(Guid userId, CancellationToken ct = default)
    {
        bool totpEnabled = await _totp.IsEnabledAsync(userId, ct);
        var webAuthnCreds = await _store.GetWebAuthnCredentialsAsync(userId, ct);
        var recoveryCodes = await _store.GetRecoveryCodesAsync(userId, ct);

        var methods = new List<string>();
        if (totpEnabled) methods.Add("totp");
        if (webAuthnCreds.Count > 0) methods.Add("webauthn");

        return new MfaStatus(
            TotpEnabled: totpEnabled,
            WebAuthnEnabled: webAuthnCreds.Count > 0,
            WebAuthnCredentialCount: webAuthnCreds.Count,
            RecoveryCodesRemaining: recoveryCodes.Count(c => !c.IsUsed),
            EnabledMethods: methods.ToArray());
    }

    /// <summary>
    /// Checks if user has any MFA method enrolled (used to determine login flow).
    /// </summary>
    public async Task<bool> HasMfaEnabledAsync(Guid userId, CancellationToken ct = default)
    {
        if (await _totp.IsEnabledAsync(userId, ct)) return true;
        if (await _webAuthn.IsEnabledAsync(userId, ct)) return true;
        return false;
    }

    private static string HashToken(string token)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hash);
    }
}

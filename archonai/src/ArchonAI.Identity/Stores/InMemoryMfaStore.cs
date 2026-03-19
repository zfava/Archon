using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;

namespace ArchonAI.Identity.Stores;

public sealed class InMemoryMfaStore : IMfaStore
{
    private readonly ConcurrentDictionary<Guid, TotpCredential> _totpByUserId = new();
    private readonly ConcurrentDictionary<Guid, WebAuthnCredential> _webAuthnById = new();
    private readonly ConcurrentDictionary<Guid, MfaRecoveryCode> _recoveryCodes = new();
    private readonly ConcurrentDictionary<Guid, MfaChallenge> _challenges = new();
    private readonly ConcurrentDictionary<Guid, MfaPolicy> _policies = new();

    // ── TOTP ────────────────────────────────────────────────────
    public Task<TotpCredential?> GetTotpCredentialAsync(Guid userId, CancellationToken ct = default)
    {
        _totpByUserId.TryGetValue(userId, out var cred);
        return Task.FromResult(cred);
    }

    public Task CreateTotpCredentialAsync(TotpCredential credential, CancellationToken ct = default)
    {
        _totpByUserId[credential.UserId] = credential;
        return Task.CompletedTask;
    }

    public Task UpdateTotpCredentialAsync(TotpCredential credential, CancellationToken ct = default)
    {
        _totpByUserId[credential.UserId] = credential;
        return Task.CompletedTask;
    }

    public Task DeleteTotpCredentialAsync(Guid userId, CancellationToken ct = default)
    {
        _totpByUserId.TryRemove(userId, out _);
        return Task.CompletedTask;
    }

    // ── WebAuthn ────────────────────────────────────────────────
    public Task<IReadOnlyList<WebAuthnCredential>> GetWebAuthnCredentialsAsync(Guid userId, CancellationToken ct = default)
    {
        IReadOnlyList<WebAuthnCredential> result = _webAuthnById.Values
            .Where(c => c.UserId == userId).ToList();
        return Task.FromResult(result);
    }

    public Task<WebAuthnCredential?> GetWebAuthnCredentialByIdAsync(byte[] credentialId, CancellationToken ct = default)
    {
        var result = _webAuthnById.Values
            .FirstOrDefault(c => c.CredentialId.SequenceEqual(credentialId));
        return Task.FromResult(result);
    }

    public Task CreateWebAuthnCredentialAsync(WebAuthnCredential credential, CancellationToken ct = default)
    {
        _webAuthnById[credential.Id] = credential;
        return Task.CompletedTask;
    }

    public Task UpdateWebAuthnCredentialAsync(WebAuthnCredential credential, CancellationToken ct = default)
    {
        _webAuthnById[credential.Id] = credential;
        return Task.CompletedTask;
    }

    public Task DeleteWebAuthnCredentialAsync(Guid credentialId, CancellationToken ct = default)
    {
        _webAuthnById.TryRemove(credentialId, out _);
        return Task.CompletedTask;
    }

    // ── Recovery codes ──────────────────────────────────────────
    public Task<IReadOnlyList<MfaRecoveryCode>> GetRecoveryCodesAsync(Guid userId, CancellationToken ct = default)
    {
        IReadOnlyList<MfaRecoveryCode> result = _recoveryCodes.Values
            .Where(c => c.UserId == userId).ToList();
        return Task.FromResult(result);
    }

    public Task CreateRecoveryCodesAsync(IEnumerable<MfaRecoveryCode> codes, CancellationToken ct = default)
    {
        foreach (var code in codes)
            _recoveryCodes[code.Id] = code;
        return Task.CompletedTask;
    }

    public Task MarkRecoveryCodeUsedAsync(Guid codeId, CancellationToken ct = default)
    {
        if (_recoveryCodes.TryGetValue(codeId, out var code))
            _recoveryCodes[codeId] = code with { IsUsed = true, UsedAtUtc = DateTimeOffset.UtcNow };
        return Task.CompletedTask;
    }

    public Task DeleteAllRecoveryCodesAsync(Guid userId, CancellationToken ct = default)
    {
        foreach (var key in _recoveryCodes.Where(c => c.Value.UserId == userId).Select(c => c.Key).ToList())
            _recoveryCodes.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    // ── MFA challenges ──────────────────────────────────────────
    public Task CreateMfaChallengeAsync(MfaChallenge challenge, CancellationToken ct = default)
    {
        _challenges[challenge.Id] = challenge;
        return Task.CompletedTask;
    }

    public Task<MfaChallenge?> GetMfaChallengeByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var challenge = _challenges.Values.FirstOrDefault(c =>
            c.TokenHash == tokenHash && !c.IsUsed && c.ExpiresAtUtc > DateTimeOffset.UtcNow);
        return Task.FromResult(challenge);
    }

    public Task MarkMfaChallengeUsedAsync(Guid challengeId, CancellationToken ct = default)
    {
        if (_challenges.TryGetValue(challengeId, out var challenge))
            _challenges[challengeId] = challenge with { IsUsed = true };
        return Task.CompletedTask;
    }

    // ── MFA policy ──────────────────────────────────────────────
    public Task<MfaPolicy?> GetMfaPolicyAsync(Guid organizationId, CancellationToken ct = default)
    {
        _policies.TryGetValue(organizationId, out var policy);
        return Task.FromResult(policy);
    }

    public Task UpsertMfaPolicyAsync(MfaPolicy policy, CancellationToken ct = default)
    {
        _policies[policy.OrganizationId] = policy;
        return Task.CompletedTask;
    }
}

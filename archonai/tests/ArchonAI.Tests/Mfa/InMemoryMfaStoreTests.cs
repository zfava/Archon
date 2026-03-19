using ArchonAI.Core.Models.Identity;
using ArchonAI.Identity.Stores;

namespace ArchonAI.Tests.Mfa;

public sealed class InMemoryMfaStoreTests
{
    private readonly InMemoryMfaStore _store = new();

    // ── TOTP Credential Store ───────────────────────────────────

    [Fact]
    public async Task TotpCredential_CreateAndRetrieve()
    {
        var userId = Guid.NewGuid();
        var cred = new TotpCredential(Guid.NewGuid(), userId, "encrypted", false, DateTimeOffset.UtcNow);

        await _store.CreateTotpCredentialAsync(cred);
        var retrieved = await _store.GetTotpCredentialAsync(userId);

        Assert.NotNull(retrieved);
        Assert.Equal(cred.Id, retrieved.Id);
    }

    [Fact]
    public async Task TotpCredential_Update()
    {
        var userId = Guid.NewGuid();
        var cred = new TotpCredential(Guid.NewGuid(), userId, "encrypted", false, DateTimeOffset.UtcNow);
        await _store.CreateTotpCredentialAsync(cred);

        await _store.UpdateTotpCredentialAsync(cred with { IsVerified = true });
        var retrieved = await _store.GetTotpCredentialAsync(userId);

        Assert.True(retrieved!.IsVerified);
    }

    [Fact]
    public async Task TotpCredential_Delete()
    {
        var userId = Guid.NewGuid();
        await _store.CreateTotpCredentialAsync(
            new TotpCredential(Guid.NewGuid(), userId, "enc", false, DateTimeOffset.UtcNow));

        await _store.DeleteTotpCredentialAsync(userId);
        Assert.Null(await _store.GetTotpCredentialAsync(userId));
    }

    // ── WebAuthn Credential Store ───────────────────────────────

    [Fact]
    public async Task WebAuthnCredential_CreateAndList()
    {
        var userId = Guid.NewGuid();
        var cred = new WebAuthnCredential(
            Guid.NewGuid(), userId, [1, 2, 3], [4, 5, 6], 0, "Key", DateTimeOffset.UtcNow, null);

        await _store.CreateWebAuthnCredentialAsync(cred);
        var list = await _store.GetWebAuthnCredentialsAsync(userId);

        Assert.Single(list);
        Assert.Equal(cred.Id, list[0].Id);
    }

    [Fact]
    public async Task WebAuthnCredential_GetByCredentialId()
    {
        byte[] credId = [1, 2, 3, 4];
        await _store.CreateWebAuthnCredentialAsync(
            new WebAuthnCredential(Guid.NewGuid(), Guid.NewGuid(), credId, [5, 6], 0, "Key", DateTimeOffset.UtcNow, null));

        var found = await _store.GetWebAuthnCredentialByIdAsync(credId);
        Assert.NotNull(found);

        var notFound = await _store.GetWebAuthnCredentialByIdAsync([9, 9, 9]);
        Assert.Null(notFound);
    }

    [Fact]
    public async Task WebAuthnCredential_Delete()
    {
        var userId = Guid.NewGuid();
        var credId = Guid.NewGuid();
        await _store.CreateWebAuthnCredentialAsync(
            new WebAuthnCredential(credId, userId, [1], [2], 0, "Key", DateTimeOffset.UtcNow, null));

        await _store.DeleteWebAuthnCredentialAsync(credId);
        var list = await _store.GetWebAuthnCredentialsAsync(userId);
        Assert.Empty(list);
    }

    // ── Recovery Codes Store ────────────────────────────────────

    [Fact]
    public async Task RecoveryCodes_CreateAndList()
    {
        var userId = Guid.NewGuid();
        var codes = Enumerable.Range(0, 3).Select(i =>
            new MfaRecoveryCode(Guid.NewGuid(), userId, $"hash-{i}", false, DateTimeOffset.UtcNow, null)).ToList();

        await _store.CreateRecoveryCodesAsync(codes);
        var list = await _store.GetRecoveryCodesAsync(userId);

        Assert.Equal(3, list.Count);
    }

    [Fact]
    public async Task RecoveryCodes_MarkUsed()
    {
        var userId = Guid.NewGuid();
        var codeId = Guid.NewGuid();
        await _store.CreateRecoveryCodesAsync([
            new MfaRecoveryCode(codeId, userId, "hash", false, DateTimeOffset.UtcNow, null)
        ]);

        await _store.MarkRecoveryCodeUsedAsync(codeId);
        var list = await _store.GetRecoveryCodesAsync(userId);

        Assert.Single(list);
        Assert.True(list[0].IsUsed);
        Assert.NotNull(list[0].UsedAtUtc);
    }

    [Fact]
    public async Task RecoveryCodes_DeleteAll()
    {
        var userId = Guid.NewGuid();
        await _store.CreateRecoveryCodesAsync([
            new MfaRecoveryCode(Guid.NewGuid(), userId, "h1", false, DateTimeOffset.UtcNow, null),
            new MfaRecoveryCode(Guid.NewGuid(), userId, "h2", false, DateTimeOffset.UtcNow, null),
        ]);

        // Another user's codes should not be affected
        var otherUserId = Guid.NewGuid();
        await _store.CreateRecoveryCodesAsync([
            new MfaRecoveryCode(Guid.NewGuid(), otherUserId, "h3", false, DateTimeOffset.UtcNow, null),
        ]);

        await _store.DeleteAllRecoveryCodesAsync(userId);
        Assert.Empty(await _store.GetRecoveryCodesAsync(userId));
        Assert.Single(await _store.GetRecoveryCodesAsync(otherUserId));
    }

    // ── MFA Challenge Store ─────────────────────────────────────

    [Fact]
    public async Task MfaChallenge_CreateAndFind()
    {
        var challenge = new MfaChallenge(
            Guid.NewGuid(), Guid.NewGuid(), "token-hash", ["totp"],
            DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow, false);

        await _store.CreateMfaChallengeAsync(challenge);
        var found = await _store.GetMfaChallengeByHashAsync("token-hash");

        Assert.NotNull(found);
        Assert.Equal(challenge.Id, found.Id);
    }

    [Fact]
    public async Task MfaChallenge_MarkUsed_ExcludesFromSearch()
    {
        var challenge = new MfaChallenge(
            Guid.NewGuid(), Guid.NewGuid(), "used-hash", ["totp"],
            DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow, false);

        await _store.CreateMfaChallengeAsync(challenge);
        await _store.MarkMfaChallengeUsedAsync(challenge.Id);

        var found = await _store.GetMfaChallengeByHashAsync("used-hash");
        Assert.Null(found);
    }

    [Fact]
    public async Task MfaChallenge_Expired_ExcludesFromSearch()
    {
        var challenge = new MfaChallenge(
            Guid.NewGuid(), Guid.NewGuid(), "expired-hash", ["totp"],
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(-6), false);

        await _store.CreateMfaChallengeAsync(challenge);
        var found = await _store.GetMfaChallengeByHashAsync("expired-hash");
        Assert.Null(found);
    }
}

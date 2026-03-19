using ArchonAI.Identity.Mfa;
using ArchonAI.Identity.Stores;
using Microsoft.Extensions.Logging.Abstractions;
using OtpNet;

namespace ArchonAI.Tests.Mfa;

public sealed class MfaChallengeServiceTests
{
    private readonly MfaChallengeService _challenge;
    private readonly TotpService _totp;
    private readonly WebAuthnService _webAuthn;
    private readonly InMemoryMfaStore _store;

    public MfaChallengeServiceTests()
    {
        _store = new InMemoryMfaStore();
        _totp = new TotpService(_store, NullLogger<TotpService>.Instance);
        _webAuthn = new WebAuthnService(_store, NullLogger<WebAuthnService>.Instance);
        _challenge = new MfaChallengeService(
            _store, _totp, _webAuthn, NullLogger<MfaChallengeService>.Instance);
    }

    private async Task<Guid> SetupTotpUser()
    {
        var userId = Guid.NewGuid();
        var setup = await _totp.GenerateSetupAsync(userId, "user@test.com");
        Assert.NotNull(setup);
        string code = GenerateValidCode(setup.Value.OtpAuthUri);
        await _totp.VerifySetupAsync(userId, code);
        return userId;
    }

    private static string GenerateValidCode(string otpAuthUri)
    {
        var uri = new Uri(otpAuthUri);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        string secret = query["secret"]!;
        byte[] secretBytes = Base32Encoding.ToBytes(secret);
        var totp = new Totp(secretBytes, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        return totp.ComputeTotp();
    }

    // ── CreateChallenge ─────────────────────────────────────────

    [Fact]
    public async Task CreateChallenge_NoMfa_ReturnsNull()
    {
        var result = await _challenge.CreateChallengeAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateChallenge_WithTotp_ReturnsTotpAndRecovery()
    {
        var userId = await SetupTotpUser();

        var result = await _challenge.CreateChallengeAsync(userId);
        Assert.NotNull(result);
        var (mfaToken, methods) = result.Value;

        Assert.NotEmpty(mfaToken);
        Assert.Contains("totp", methods);
        Assert.Contains("recovery", methods);
    }

    [Fact]
    public async Task CreateChallenge_WithWebAuthn_ReturnsWebAuthnAndRecovery()
    {
        var userId = Guid.NewGuid();
        await _webAuthn.CompleteRegistrationAsync(
            userId, "Key", Guid.NewGuid().ToByteArray(), new byte[65], 0);

        var result = await _challenge.CreateChallengeAsync(userId);
        Assert.NotNull(result);
        Assert.Contains("webauthn", result.Value.Methods);
        // No recovery codes for WebAuthn-only (no recovery codes generated)
        Assert.DoesNotContain("recovery", result.Value.Methods);
    }

    [Fact]
    public async Task CreateChallenge_WithBothMethods_ReturnsAll()
    {
        var userId = await SetupTotpUser();
        await _webAuthn.CompleteRegistrationAsync(
            userId, "Key", Guid.NewGuid().ToByteArray(), new byte[65], 0);

        var result = await _challenge.CreateChallengeAsync(userId);
        Assert.NotNull(result);
        Assert.Contains("totp", result.Value.Methods);
        Assert.Contains("webauthn", result.Value.Methods);
        Assert.Contains("recovery", result.Value.Methods);
    }

    // ── ValidateChallenge ───────────────────────────────────────

    [Fact]
    public async Task ValidateChallenge_ValidToken_ReturnsUserId()
    {
        var userId = await SetupTotpUser();
        var challenge = await _challenge.CreateChallengeAsync(userId);
        Assert.NotNull(challenge);

        var validatedUserId = await _challenge.ValidateChallengeAsync(challenge.Value.MfaToken);
        Assert.NotNull(validatedUserId);
        Assert.Equal(userId, validatedUserId.Value);
    }

    [Fact]
    public async Task ValidateChallenge_SingleUse_SecondAttemptFails()
    {
        var userId = await SetupTotpUser();
        var challenge = await _challenge.CreateChallengeAsync(userId);
        Assert.NotNull(challenge);

        Assert.NotNull(await _challenge.ValidateChallengeAsync(challenge.Value.MfaToken));
        Assert.Null(await _challenge.ValidateChallengeAsync(challenge.Value.MfaToken));
    }

    [Fact]
    public async Task ValidateChallenge_InvalidToken_ReturnsNull()
    {
        var result = await _challenge.ValidateChallengeAsync("totally-invalid-token");
        Assert.Null(result);
    }

    // ── VerifyRecoveryCode ──────────────────────────────────────

    [Fact]
    public async Task VerifyRecoveryCode_ValidCode_Succeeds()
    {
        var userId = Guid.NewGuid();
        var setup = await _totp.GenerateSetupAsync(userId, "user@test.com");
        Assert.NotNull(setup);

        // Use a recovery code
        string code = setup.Value.RecoveryCodes[0];
        Assert.True(await _challenge.VerifyRecoveryCodeAsync(userId, code));
    }

    [Fact]
    public async Task VerifyRecoveryCode_SingleUse_SecondAttemptFails()
    {
        var userId = Guid.NewGuid();
        var setup = await _totp.GenerateSetupAsync(userId, "user@test.com");
        Assert.NotNull(setup);

        string code = setup.Value.RecoveryCodes[0];
        Assert.True(await _challenge.VerifyRecoveryCodeAsync(userId, code));
        Assert.False(await _challenge.VerifyRecoveryCodeAsync(userId, code));
    }

    [Fact]
    public async Task VerifyRecoveryCode_DifferentCodesWork()
    {
        var userId = Guid.NewGuid();
        var setup = await _totp.GenerateSetupAsync(userId, "user@test.com");
        Assert.NotNull(setup);

        Assert.True(await _challenge.VerifyRecoveryCodeAsync(userId, setup.Value.RecoveryCodes[0]));
        Assert.True(await _challenge.VerifyRecoveryCodeAsync(userId, setup.Value.RecoveryCodes[1]));
        Assert.True(await _challenge.VerifyRecoveryCodeAsync(userId, setup.Value.RecoveryCodes[2]));
    }

    [Fact]
    public async Task VerifyRecoveryCode_InvalidCode_Fails()
    {
        var userId = Guid.NewGuid();
        await _totp.GenerateSetupAsync(userId, "user@test.com");

        Assert.False(await _challenge.VerifyRecoveryCodeAsync(userId, "xxxx-xxxx"));
    }

    // ── GetMfaStatus ────────────────────────────────────────────

    [Fact]
    public async Task GetMfaStatus_NoEnrollment_AllDisabled()
    {
        var status = await _challenge.GetMfaStatusAsync(Guid.NewGuid());

        Assert.False(status.TotpEnabled);
        Assert.False(status.WebAuthnEnabled);
        Assert.Equal(0, status.WebAuthnCredentialCount);
        Assert.Equal(0, status.RecoveryCodesRemaining);
        Assert.Empty(status.EnabledMethods);
    }

    [Fact]
    public async Task GetMfaStatus_TotpEnrolled_ReflectsCorrectly()
    {
        var userId = await SetupTotpUser();
        var status = await _challenge.GetMfaStatusAsync(userId);

        Assert.True(status.TotpEnabled);
        Assert.False(status.WebAuthnEnabled);
        Assert.Contains("totp", status.EnabledMethods);
        Assert.Equal(10, status.RecoveryCodesRemaining);
    }

    [Fact]
    public async Task GetMfaStatus_WebAuthnEnrolled_ReflectsCorrectly()
    {
        var userId = Guid.NewGuid();
        await _webAuthn.CompleteRegistrationAsync(
            userId, "Key 1", Guid.NewGuid().ToByteArray(), new byte[65], 0);
        await _webAuthn.CompleteRegistrationAsync(
            userId, "Key 2", Guid.NewGuid().ToByteArray(), new byte[65], 0);

        var status = await _challenge.GetMfaStatusAsync(userId);
        Assert.True(status.WebAuthnEnabled);
        Assert.Equal(2, status.WebAuthnCredentialCount);
        Assert.Contains("webauthn", status.EnabledMethods);
    }

    [Fact]
    public async Task GetMfaStatus_RecoveryCodesDecrementAfterUse()
    {
        var userId = Guid.NewGuid();
        var setup = await _totp.GenerateSetupAsync(userId, "user@test.com");
        Assert.NotNull(setup);
        string code = GenerateValidCode(setup.Value.OtpAuthUri);
        await _totp.VerifySetupAsync(userId, code);

        Assert.Equal(10, (await _challenge.GetMfaStatusAsync(userId)).RecoveryCodesRemaining);

        await _challenge.VerifyRecoveryCodeAsync(userId, setup.Value.RecoveryCodes[0]);
        Assert.Equal(9, (await _challenge.GetMfaStatusAsync(userId)).RecoveryCodesRemaining);
    }

    // ── HasMfaEnabled ───────────────────────────────────────────

    [Fact]
    public async Task HasMfaEnabled_NoEnrollment_False()
    {
        Assert.False(await _challenge.HasMfaEnabledAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task HasMfaEnabled_TotpEnrolled_True()
    {
        var userId = await SetupTotpUser();
        Assert.True(await _challenge.HasMfaEnabledAsync(userId));
    }

    [Fact]
    public async Task HasMfaEnabled_WebAuthnEnrolled_True()
    {
        var userId = Guid.NewGuid();
        await _webAuthn.CompleteRegistrationAsync(
            userId, "Key", Guid.NewGuid().ToByteArray(), new byte[65], 0);
        Assert.True(await _challenge.HasMfaEnabledAsync(userId));
    }
}

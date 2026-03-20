using ArchonAI.Identity.Mfa;
using ArchonAI.Identity.Stores;
using Microsoft.Extensions.Logging.Abstractions;
using OtpNet;

namespace ArchonAI.Tests.Mfa;

public sealed class TotpServiceTests
{
    private readonly TotpService _totp;
    private readonly InMemoryMfaStore _store;

    public TotpServiceTests()
    {
        _store = new InMemoryMfaStore();
        _totp = new TotpService(_store, new TestTotpSecretEncryptor(), NullLogger<TotpService>.Instance);
    }

    private static string GenerateValidCode(string otpAuthUri)
    {
        // Extract base32 secret from otpauth URI
        var uri = new Uri(otpAuthUri);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        string secret = query["secret"]!;
        byte[] secretBytes = Base32Encoding.ToBytes(secret);
        var totp = new Totp(secretBytes, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        return totp.ComputeTotp();
    }

    [Fact]
    public async Task GenerateSetup_ReturnsOtpAuthUriAndRecoveryCodes()
    {
        var userId = Guid.NewGuid();
        var result = await _totp.GenerateSetupAsync(userId, "user@test.com");

        Assert.NotNull(result);
        var (otpAuthUri, recoveryCodes) = result.Value;
        Assert.StartsWith("otpauth://totp/ArchonAI:", otpAuthUri);
        Assert.Contains("user%40test.com", otpAuthUri);
        Assert.Contains("secret=", otpAuthUri);
        Assert.Contains("issuer=ArchonAI", otpAuthUri);
        Assert.Equal(10, recoveryCodes.Length);
    }

    [Fact]
    public async Task GenerateSetup_RecoveryCodes_HaveDashFormat()
    {
        var userId = Guid.NewGuid();
        var result = await _totp.GenerateSetupAsync(userId, "user@test.com");

        Assert.NotNull(result);
        foreach (var code in result.Value.RecoveryCodes)
        {
            Assert.Matches(@"^[0-9a-f]{4}-[0-9a-f]{4}$", code);
        }
    }

    [Fact]
    public async Task GenerateSetup_CreatesUnverifiedCredential()
    {
        var userId = Guid.NewGuid();
        await _totp.GenerateSetupAsync(userId, "user@test.com");

        Assert.False(await _totp.IsEnabledAsync(userId));
    }

    [Fact]
    public async Task GenerateSetup_WhenAlreadyVerified_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        var setup = await _totp.GenerateSetupAsync(userId, "user@test.com");
        Assert.NotNull(setup);

        string code = GenerateValidCode(setup.Value.OtpAuthUri);
        Assert.True(await _totp.VerifySetupAsync(userId, code));

        // Second setup attempt should fail
        var secondSetup = await _totp.GenerateSetupAsync(userId, "user@test.com");
        Assert.Null(secondSetup);
    }

    [Fact]
    public async Task GenerateSetup_ReplacesUnverifiedCredential()
    {
        var userId = Guid.NewGuid();
        var first = await _totp.GenerateSetupAsync(userId, "user@test.com");
        var second = await _totp.GenerateSetupAsync(userId, "user@test.com");

        Assert.NotNull(first);
        Assert.NotNull(second);
        // Different secrets each time
        Assert.NotEqual(first.Value.OtpAuthUri, second.Value.OtpAuthUri);
    }

    [Fact]
    public async Task VerifySetup_ValidCode_EnablesTotp()
    {
        var userId = Guid.NewGuid();
        var setup = await _totp.GenerateSetupAsync(userId, "user@test.com");
        Assert.NotNull(setup);

        string code = GenerateValidCode(setup.Value.OtpAuthUri);
        bool verified = await _totp.VerifySetupAsync(userId, code);

        Assert.True(verified);
        Assert.True(await _totp.IsEnabledAsync(userId));
    }

    [Fact]
    public async Task VerifySetup_InvalidCode_DoesNotEnable()
    {
        var userId = Guid.NewGuid();
        await _totp.GenerateSetupAsync(userId, "user@test.com");

        bool verified = await _totp.VerifySetupAsync(userId, "000000");
        Assert.False(verified);
        Assert.False(await _totp.IsEnabledAsync(userId));
    }

    [Fact]
    public async Task VerifySetup_AlreadyVerified_ReturnsFalse()
    {
        var userId = Guid.NewGuid();
        var setup = await _totp.GenerateSetupAsync(userId, "user@test.com");
        Assert.NotNull(setup);

        string code = GenerateValidCode(setup.Value.OtpAuthUri);
        Assert.True(await _totp.VerifySetupAsync(userId, code));

        // Second verify should fail (already verified)
        Assert.False(await _totp.VerifySetupAsync(userId, code));
    }

    [Fact]
    public async Task Verify_AfterEnrollment_ValidCode_ReturnsTrue()
    {
        var userId = Guid.NewGuid();
        var setup = await _totp.GenerateSetupAsync(userId, "user@test.com");
        Assert.NotNull(setup);

        string setupCode = GenerateValidCode(setup.Value.OtpAuthUri);
        await _totp.VerifySetupAsync(userId, setupCode);

        // Login verification
        string loginCode = GenerateValidCode(setup.Value.OtpAuthUri);
        Assert.True(await _totp.VerifyAsync(userId, loginCode));
    }

    [Fact]
    public async Task Verify_InvalidCode_ReturnsFalse()
    {
        var userId = Guid.NewGuid();
        var setup = await _totp.GenerateSetupAsync(userId, "user@test.com");
        Assert.NotNull(setup);

        string code = GenerateValidCode(setup.Value.OtpAuthUri);
        await _totp.VerifySetupAsync(userId, code);

        Assert.False(await _totp.VerifyAsync(userId, "999999"));
    }

    [Fact]
    public async Task Verify_UnverifiedCredential_ReturnsFalse()
    {
        var userId = Guid.NewGuid();
        var setup = await _totp.GenerateSetupAsync(userId, "user@test.com");
        Assert.NotNull(setup);

        // Don't call VerifySetupAsync - try to verify login directly
        string code = GenerateValidCode(setup.Value.OtpAuthUri);
        Assert.False(await _totp.VerifyAsync(userId, code));
    }

    [Fact]
    public async Task Verify_NoCredential_ReturnsFalse()
    {
        Assert.False(await _totp.VerifyAsync(Guid.NewGuid(), "123456"));
    }

    [Fact]
    public async Task Disable_WithValidCode_RemovesCredential()
    {
        var userId = Guid.NewGuid();
        var setup = await _totp.GenerateSetupAsync(userId, "user@test.com");
        Assert.NotNull(setup);

        string code = GenerateValidCode(setup.Value.OtpAuthUri);
        await _totp.VerifySetupAsync(userId, code);
        Assert.True(await _totp.IsEnabledAsync(userId));

        string disableCode = GenerateValidCode(setup.Value.OtpAuthUri);
        Assert.True(await _totp.DisableAsync(userId, disableCode));
        Assert.False(await _totp.IsEnabledAsync(userId));
    }

    [Fact]
    public async Task Disable_WithInvalidCode_Rejected()
    {
        var userId = Guid.NewGuid();
        var setup = await _totp.GenerateSetupAsync(userId, "user@test.com");
        Assert.NotNull(setup);

        string code = GenerateValidCode(setup.Value.OtpAuthUri);
        await _totp.VerifySetupAsync(userId, code);

        Assert.False(await _totp.DisableAsync(userId, "000000"));
        Assert.True(await _totp.IsEnabledAsync(userId));
    }

    [Fact]
    public async Task Disable_NotEnrolled_ReturnsFalse()
    {
        Assert.False(await _totp.DisableAsync(Guid.NewGuid(), "123456"));
    }

    // ── Recovery Code Hashing ────────────────────────────────────

    [Fact]
    public void RecoveryCodeHash_RoundTrips()
    {
        string code = "abcd-ef01";
        string hash = TotpService.HashRecoveryCode(code);
        Assert.True(TotpService.VerifyRecoveryCode(code, hash));
    }

    [Fact]
    public void RecoveryCodeHash_WrongCode_Fails()
    {
        string hash = TotpService.HashRecoveryCode("abcd-ef01");
        Assert.False(TotpService.VerifyRecoveryCode("xxxx-yyyy", hash));
    }

    [Fact]
    public void RecoveryCodeHash_CaseInsensitive()
    {
        string hash = TotpService.HashRecoveryCode("ABCD-EF01");
        Assert.True(TotpService.VerifyRecoveryCode("abcd-ef01", hash));
    }

    [Fact]
    public void RecoveryCodeHash_DashInsensitive()
    {
        string hash = TotpService.HashRecoveryCode("abcd-ef01");
        Assert.True(TotpService.VerifyRecoveryCode("abcdef01", hash));
    }

    [Fact]
    public void RecoveryCodeHash_UniqueSalts()
    {
        string h1 = TotpService.HashRecoveryCode("abcd-ef01");
        string h2 = TotpService.HashRecoveryCode("abcd-ef01");
        Assert.NotEqual(h1, h2); // different salts
    }

    // ── Secret Encryption ────────────────────────────────────────

    [Fact]
    public async Task GenerateSetup_SecretIsEncryptedAtRest()
    {
        var userId = Guid.NewGuid();
        var setup = await _totp.GenerateSetupAsync(userId, "user@test.com");
        Assert.NotNull(setup);

        var credential = await _store.GetTotpCredentialAsync(userId);
        Assert.NotNull(credential);
        // Encrypted secret should be base64.base64 (IV.ciphertext)
        Assert.Contains('.', credential.EncryptedSecret);
        // Should NOT contain the plaintext base32 secret
        var uri = new Uri(setup.Value.OtpAuthUri);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        string plainSecret = query["secret"]!;
        Assert.DoesNotContain(plainSecret, credential.EncryptedSecret);
    }
}

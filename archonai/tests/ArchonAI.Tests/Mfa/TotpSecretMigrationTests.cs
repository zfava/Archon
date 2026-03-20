using ArchonAI.Core.Models.Identity;
using ArchonAI.Identity.Mfa;
using ArchonAI.Identity.Stores;
using Microsoft.Extensions.Logging.Abstractions;
using OtpNet;

namespace ArchonAI.Tests.Mfa;

/// <summary>
/// Tests proving that TOTP secrets encrypted with the legacy JWT-derived key
/// are automatically re-encrypted with the dedicated TOTP key on successful verification.
/// </summary>
public sealed class TotpSecretMigrationTests
{
    private readonly InMemoryMfaStore _store;
    private readonly TestTotpSecretEncryptor _encryptor;
    private readonly TotpService _totp;

    public TotpSecretMigrationTests()
    {
        _store = new InMemoryMfaStore();
        _encryptor = new TestTotpSecretEncryptor();
        _totp = new TotpService(_store, _encryptor, NullLogger<TotpService>.Instance);
    }

    [Fact]
    public async Task VerifyAsync_LegacySecret_ReEncryptsWithDedicatedKey()
    {
        var userId = Guid.NewGuid();
        string base32Secret = Base32Encoding.ToString(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));

        // Simulate a credential encrypted with the legacy key
        string legacyCiphertext = _encryptor.EncryptLegacy(base32Secret);
        Assert.True(_encryptor.IsLegacyEncrypted(legacyCiphertext));

        var credential = new TotpCredential(
            Id: Guid.NewGuid(),
            UserId: userId,
            EncryptedSecret: legacyCiphertext,
            IsVerified: true,
            CreatedAtUtc: DateTimeOffset.UtcNow);
        await _store.CreateTotpCredentialAsync(credential);

        // Generate a valid TOTP code from the secret
        byte[] secretBytes = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(secretBytes, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        string code = totp.ComputeTotp();

        // Verify — this should trigger re-encryption
        bool result = await _totp.VerifyAsync(userId, code);
        Assert.True(result);

        // The stored credential should now use the new format
        var updated = await _store.GetTotpCredentialAsync(userId);
        Assert.NotNull(updated);
        Assert.False(_encryptor.IsLegacyEncrypted(updated.EncryptedSecret));
        Assert.StartsWith("v1:", updated.EncryptedSecret);

        // And it should still decrypt to the same secret
        string decrypted = _encryptor.Decrypt(updated.EncryptedSecret);
        Assert.Equal(base32Secret, decrypted);
    }

    [Fact]
    public async Task VerifySetupAsync_LegacySecret_ReEncryptsWithDedicatedKey()
    {
        var userId = Guid.NewGuid();
        string base32Secret = Base32Encoding.ToString(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));

        // Simulate a credential encrypted with the legacy key, not yet verified
        string legacyCiphertext = _encryptor.EncryptLegacy(base32Secret);
        var credential = new TotpCredential(
            Id: Guid.NewGuid(),
            UserId: userId,
            EncryptedSecret: legacyCiphertext,
            IsVerified: false,
            CreatedAtUtc: DateTimeOffset.UtcNow);
        await _store.CreateTotpCredentialAsync(credential);

        // Generate valid code
        byte[] secretBytes = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(secretBytes, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        string code = totp.ComputeTotp();

        bool result = await _totp.VerifySetupAsync(userId, code);
        Assert.True(result);

        var updated = await _store.GetTotpCredentialAsync(userId);
        Assert.NotNull(updated);
        // Secret should be re-encrypted with dedicated key
        Assert.False(_encryptor.IsLegacyEncrypted(updated.EncryptedSecret));
    }

    [Fact]
    public async Task VerifyAsync_CurrentSecret_DoesNotReEncrypt()
    {
        var userId = Guid.NewGuid();

        // Use normal setup flow (creates with current key)
        var setup = await _totp.GenerateSetupAsync(userId, "user@test.com");
        Assert.NotNull(setup);

        // Verify setup
        string setupCode = GenerateValidCode(setup.Value.OtpAuthUri);
        await _totp.VerifySetupAsync(userId, setupCode);

        var before = await _store.GetTotpCredentialAsync(userId);
        Assert.NotNull(before);
        Assert.False(_encryptor.IsLegacyEncrypted(before.EncryptedSecret));

        // Login verification — should NOT change the ciphertext
        // (it's already in the current format)
        string loginCode = GenerateValidCode(setup.Value.OtpAuthUri);
        await _totp.VerifyAsync(userId, loginCode);

        var after = await _store.GetTotpCredentialAsync(userId);
        Assert.NotNull(after);
        Assert.False(_encryptor.IsLegacyEncrypted(after.EncryptedSecret));
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
}

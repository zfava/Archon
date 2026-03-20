using System.Security.Cryptography;
using System.Text;
using ArchonAI.Core.Interfaces;
using ArchonAI.Infrastructure.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArchonAI.Tests.Mfa;

/// <summary>
/// Tests for <see cref="DedicatedTotpSecretEncryptor"/> proving:
/// - Encryption/decryption round-trip with dedicated key
/// - JWT signing-key rotation does NOT break TOTP secret decryptability
/// - Legacy (JWT-derived) ciphertexts are detected and decrypted correctly
/// - Key-versioned ciphertexts include the v1: prefix
/// </summary>
public sealed class TotpSecretEncryptionTests
{
    private const string TotpKeyEnvVar = "ARCHONAI_TOTP_ENCRYPTION_KEY";
    private const string JwtKeyEnvVar = "ARCHONAI_JWT_SIGNING_KEY";

    // ── Round-trip encryption ────────────────────────────────────

    [Fact]
    public void Encrypt_Decrypt_RoundTrips()
    {
        var encryptor = CreateEncryptor(totpKey: "my-dedicated-totp-key-32chars!!!");

        string plaintext = "JBSWY3DPEHPK3PXP"; // typical base32 TOTP secret
        string ciphertext = encryptor.Encrypt(plaintext);
        string decrypted = encryptor.Decrypt(ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesVersionedFormat()
    {
        var encryptor = CreateEncryptor(totpKey: "test-key");

        string ciphertext = encryptor.Encrypt("JBSWY3DPEHPK3PXP");

        Assert.StartsWith("v1:", ciphertext);
        // Format: v1:{base64}.{base64}.{base64}
        string payload = ciphertext[3..];
        var parts = payload.Split('.');
        Assert.Equal(3, parts.Length);
    }

    [Fact]
    public void Encrypt_DifferentCiphertextsEachTime()
    {
        var encryptor = CreateEncryptor(totpKey: "test-key");
        string plaintext = "JBSWY3DPEHPK3PXP";

        string ct1 = encryptor.Encrypt(plaintext);
        string ct2 = encryptor.Encrypt(plaintext);

        Assert.NotEqual(ct1, ct2); // different random IVs
        Assert.Equal(plaintext, encryptor.Decrypt(ct1));
        Assert.Equal(plaintext, encryptor.Decrypt(ct2));
    }

    // ── JWT key rotation does NOT break TOTP ─────────────────────

    [Fact]
    public void JwtKeyRotation_DoesNotBreakTotpDecryption()
    {
        string totpKey = "stable-totp-encryption-key-here!";

        // Encrypt with JWT key = "old-jwt-key"
        var encryptorBefore = CreateEncryptor(totpKey: totpKey, jwtKey: "old-jwt-key-that-is-at-least-32-bytes-long!!");
        string ciphertext = encryptorBefore.Encrypt("JBSWY3DPEHPK3PXP");

        // Simulate JWT key rotation: JWT key changes, TOTP key stays the same
        var encryptorAfter = CreateEncryptor(totpKey: totpKey, jwtKey: "new-rotated-jwt-key-completely-different!!");
        string decrypted = encryptorAfter.Decrypt(ciphertext);

        Assert.Equal("JBSWY3DPEHPK3PXP", decrypted);
    }

    [Fact]
    public void DedicatedTotpKey_IsIndependentOfJwtKey()
    {
        // Two encryptors with same TOTP key but different JWT keys
        var enc1 = CreateEncryptor(totpKey: "same-totp-key", jwtKey: "jwt-key-alpha");
        var enc2 = CreateEncryptor(totpKey: "same-totp-key", jwtKey: "jwt-key-beta");

        string ciphertext = enc1.Encrypt("SECRET123");

        // Both can decrypt (TOTP key is the same)
        Assert.Equal("SECRET123", enc2.Decrypt(ciphertext));
    }

    // ── Legacy (JWT-derived) migration ───────────────────────────

    [Fact]
    public void IsLegacyEncrypted_DetectsLegacyFormat()
    {
        var encryptor = CreateEncryptor(totpKey: "test-key");

        // Legacy format: {base64}.{base64} (no version prefix)
        string legacy = EncryptLegacy("JBSWY3DPEHPK3PXP", "some-jwt-key-at-least-32-bytes-long!!");
        Assert.True(encryptor.IsLegacyEncrypted(legacy));

        // Current format has v1: prefix
        string current = encryptor.Encrypt("JBSWY3DPEHPK3PXP");
        Assert.False(encryptor.IsLegacyEncrypted(current));
    }

    [Fact]
    public void Decrypt_LegacyCiphertext_UsesJwtDerivedKey()
    {
        string jwtKey = "my-jwt-signing-key-at-least-32-bytes!!";
        var encryptor = CreateEncryptor(totpKey: "dedicated-totp-key", jwtKey: jwtKey);

        // Simulate a ciphertext created by the old TotpService using JWT key
        string legacyCiphertext = EncryptLegacy("JBSWY3DPEHPK3PXP", jwtKey);

        string decrypted = encryptor.Decrypt(legacyCiphertext);
        Assert.Equal("JBSWY3DPEHPK3PXP", decrypted);
    }

    [Fact]
    public void LegacySecret_CanBeReEncryptedWithDedicatedKey()
    {
        string jwtKey = "my-jwt-signing-key-at-least-32-bytes!!";
        var encryptor = CreateEncryptor(totpKey: "dedicated-totp-key", jwtKey: jwtKey);

        string legacyCiphertext = EncryptLegacy("JBSWY3DPEHPK3PXP", jwtKey);
        Assert.True(encryptor.IsLegacyEncrypted(legacyCiphertext));

        // Decrypt legacy, re-encrypt with dedicated key
        string plaintext = encryptor.Decrypt(legacyCiphertext);
        string newCiphertext = encryptor.Encrypt(plaintext);

        Assert.False(encryptor.IsLegacyEncrypted(newCiphertext));
        Assert.Equal("JBSWY3DPEHPK3PXP", encryptor.Decrypt(newCiphertext));
    }

    // ── MAC verification ─────────────────────────────────────────

    [Fact]
    public void Decrypt_TamperedCiphertext_ThrowsCryptographicException()
    {
        var encryptor = CreateEncryptor(totpKey: "test-key");
        string ciphertext = encryptor.Encrypt("JBSWY3DPEHPK3PXP");

        // Tamper with the ciphertext portion
        string payload = ciphertext[3..]; // strip "v1:"
        var parts = payload.Split('.');
        byte[] encrypted = Convert.FromBase64String(parts[1]);
        encrypted[0] ^= 0xFF; // flip a byte
        string tampered = $"v1:{parts[0]}.{Convert.ToBase64String(encrypted)}.{parts[2]}";

        Assert.Throws<CryptographicException>(() => encryptor.Decrypt(tampered));
    }

    [Fact]
    public void Decrypt_WrongKey_ThrowsCryptographicException()
    {
        var encryptor1 = CreateEncryptor(totpKey: "key-one-for-encryption");
        var encryptor2 = CreateEncryptor(totpKey: "key-two-completely-different");

        string ciphertext = encryptor1.Encrypt("JBSWY3DPEHPK3PXP");

        // Wrong key → MAC mismatch
        Assert.Throws<CryptographicException>(() => encryptor2.Decrypt(ciphertext));
    }

    // ── Fallback behavior ────────────────────────────────────────

    [Fact]
    public void NoTotpKey_FallsBackToJwtKey_InDevelopment()
    {
        string jwtKey = "jwt-fallback-key-at-least-32-bytes!!";
        // No TOTP key set, only JWT key — encryptor falls back to JWT in development
        var encryptor = CreateEncryptor(totpKey: null, jwtKey: jwtKey, isProductionLike: false);

        string ciphertext = encryptor.Encrypt("JBSWY3DPEHPK3PXP");
        Assert.StartsWith("v1:", ciphertext); // still versioned
        Assert.Equal("JBSWY3DPEHPK3PXP", encryptor.Decrypt(ciphertext));
        Assert.False(encryptor.HasDedicatedKey);
        Assert.True(encryptor.HasUsableKey);
    }

    [Fact]
    public void NoTotpKey_ThrowsInProduction()
    {
        string jwtKey = "jwt-fallback-key-at-least-32-bytes!!";
        // In production, missing TOTP key must cause Encrypt to throw — even if JWT key exists
        var encryptor = CreateEncryptor(totpKey: null, jwtKey: jwtKey, isProductionLike: true);

        Assert.False(encryptor.HasDedicatedKey);
        Assert.False(encryptor.HasUsableKey);
        Assert.Throws<InvalidOperationException>(() => encryptor.Encrypt("JBSWY3DPEHPK3PXP"));
    }

    [Fact]
    public void DedicatedKey_WorksInProduction()
    {
        var encryptor = CreateEncryptor(
            totpKey: "dedicated-totp-key-for-production!", jwtKey: "some-jwt-key", isProductionLike: true);

        Assert.True(encryptor.HasDedicatedKey);
        Assert.True(encryptor.HasUsableKey);

        string ciphertext = encryptor.Encrypt("JBSWY3DPEHPK3PXP");
        Assert.Equal("JBSWY3DPEHPK3PXP", encryptor.Decrypt(ciphertext));
    }

    [Fact]
    public void NoKeys_ReportsNotUsable()
    {
        var encryptor = CreateEncryptor(totpKey: null, jwtKey: null, isProductionLike: false);

        Assert.False(encryptor.HasDedicatedKey);
        Assert.False(encryptor.HasUsableKey);
        Assert.Throws<InvalidOperationException>(() => encryptor.Encrypt("JBSWY3DPEHPK3PXP"));
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static DedicatedTotpSecretEncryptor CreateEncryptor(
        string? totpKey = null, string? jwtKey = null, bool isProductionLike = false)
    {
        var provider = new StubSecretProvider(totpKey, jwtKey);
        var posture = new ArchonAI.Common.EnvironmentPosture { IsProductionLike = isProductionLike };
        return new DedicatedTotpSecretEncryptor(provider, NullLogger<DedicatedTotpSecretEncryptor>.Instance, posture);
    }

    /// <summary>
    /// Simulates the old TotpService.EncryptSecret() that used SHA256(JWT key) directly.
    /// </summary>
    private static string EncryptLegacy(string plaintext, string jwtKey)
    {
        byte[] keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(jwtKey));
        byte[] iv = RandomNumberGenerator.GetBytes(16);
        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.IV = iv;
        using var encryptor = aes.CreateEncryptor();
        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        return $"{Convert.ToBase64String(iv)}.{Convert.ToBase64String(encrypted)}";
    }

    private sealed class StubSecretProvider : ISecretProvider
    {
        private readonly string? _totpKey;
        private readonly string? _jwtKey;

        public StubSecretProvider(string? totpKey, string? jwtKey)
        {
            _totpKey = totpKey;
            _jwtKey = jwtKey;
        }

        public bool SupportsRotation => false;

        public string? GetSecret(string key) => key switch
        {
            "ARCHONAI_TOTP_ENCRYPTION_KEY" => _totpKey,
            "ARCHONAI_JWT_SIGNING_KEY" => _jwtKey,
            _ => null
        };

        public string GetRequiredSecret(string key) =>
            GetSecret(key) ?? throw new InvalidOperationException($"Secret '{key}' not found.");
    }
}

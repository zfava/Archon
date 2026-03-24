using System.Security.Cryptography;
using System.Text;
using ArchonAI.Core.Interfaces;

namespace ArchonAI.Tests.Mfa;

/// <summary>
/// Test-only <see cref="ITotpSecretEncryptor"/> that uses a fixed key for deterministic behavior.
/// Produces versioned ciphertext (v1: prefix) identical in format to <c>DedicatedTotpSecretEncryptor</c>.
/// </summary>
internal sealed class TestTotpSecretEncryptor : ITotpSecretEncryptor
{
    private const string CurrentVersion = "v1";
    private static readonly byte[] TestKey = SHA256.HashData("test-totp-encryption-key"u8);

    public string Encrypt(string plaintext)
    {
        byte[] iv = RandomNumberGenerator.GetBytes(16);
        using var aes = Aes.Create();
        aes.Key = TestKey;
        aes.IV = iv;
        using var encryptor = aes.CreateEncryptor();
        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        return $"{CurrentVersion}:{Convert.ToBase64String(iv)}.{Convert.ToBase64String(encrypted)}.{Convert.ToBase64String(new byte[32])}";
    }

    public string Decrypt(string ciphertext)
    {
        if (IsLegacyEncrypted(ciphertext))
            return DecryptLegacy(ciphertext);

        string payload = ciphertext[3..]; // strip "v1:"
        var parts = payload.Split('.');
        byte[] iv = Convert.FromBase64String(parts[0]);
        byte[] encrypted = Convert.FromBase64String(parts[1]);

        using var aes = Aes.Create();
        aes.Key = TestKey;
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        byte[] plainBytes = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    public bool IsLegacyEncrypted(string ciphertext)
    {
        return !ciphertext.StartsWith("v1:", StringComparison.Ordinal);
    }

    /// <summary>
    /// Produces a legacy-format ciphertext (no version prefix, JWT-derived key style).
    /// Used by tests to simulate pre-migration credentials.
    /// </summary>
    public string EncryptLegacy(string plaintext)
    {
        byte[] legacyKey = SHA256.HashData("legacy-jwt-key"u8);
        byte[] iv = RandomNumberGenerator.GetBytes(16);
        using var aes = Aes.Create();
        aes.Key = legacyKey;
        aes.IV = iv;
        using var encryptor = aes.CreateEncryptor();
        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        return $"{Convert.ToBase64String(iv)}.{Convert.ToBase64String(encrypted)}";
    }

    /// <summary>
    /// Decrypts legacy-format ciphertext (for test verification).
    /// </summary>
    private static string DecryptLegacy(string ciphertext)
    {
        byte[] legacyKey = SHA256.HashData("legacy-jwt-key"u8);
        var parts = ciphertext.Split('.');
        byte[] iv = Convert.FromBase64String(parts[0]);
        byte[] encrypted = Convert.FromBase64String(parts[1]);
        using var aes = Aes.Create();
        aes.Key = legacyKey;
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        byte[] plainBytes = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }
}

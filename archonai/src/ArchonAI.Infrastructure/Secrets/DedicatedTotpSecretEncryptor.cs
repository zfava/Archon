using System.Security.Cryptography;
using System.Text;
using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Infrastructure.Secrets;

/// <summary>
/// Encrypts TOTP secrets using a dedicated encryption key (<c>ARCHONAI_TOTP_ENCRYPTION_KEY</c>)
/// that is completely independent of the JWT signing-key lifecycle.
///
/// Design:
/// - AES-256-CBC with HMAC-SHA256 for authenticated encryption (Encrypt-then-MAC).
/// - Key material derived via HKDF-SHA256 from the raw secret, producing separate
///   256-bit encryption and MAC keys.
/// - Ciphertext format: <c>v1:{Base64(IV)}.{Base64(Ciphertext)}.{Base64(HMAC)}</c>
/// - The <c>v1:</c> prefix enables future key rotation: a <c>v2:</c> prefix would
///   use a new key while <c>v1:</c> ciphertexts remain decryptable with the old key.
///
/// Legacy migration:
/// - Ciphertexts without a version prefix (format: <c>{Base64(IV)}.{Base64(Ciphertext)}</c>)
///   are detected as legacy and decrypted using the JWT-derived key (<c>ARCHONAI_JWT_SIGNING_KEY</c>).
/// - On successful TOTP verification, callers should re-encrypt with the current key.
/// </summary>
public sealed class DedicatedTotpSecretEncryptor : ITotpSecretEncryptor
{
    private const string CurrentVersion = "v1";
    private const string TotpKeyEnvVar = "ARCHONAI_TOTP_ENCRYPTION_KEY";
    private const string JwtKeyEnvVar = "ARCHONAI_JWT_SIGNING_KEY";
    private const string DevFallbackKey = "default-dev-key-not-for-production!!";

    // HKDF info strings to derive separate encryption and MAC keys
    private static readonly byte[] EncKeyInfo = "archonai-totp-enc"u8.ToArray();
    private static readonly byte[] MacKeyInfo = "archonai-totp-mac"u8.ToArray();

    private readonly ISecretProvider _secretProvider;
    private readonly ILogger<DedicatedTotpSecretEncryptor> _logger;

    public DedicatedTotpSecretEncryptor(
        ISecretProvider secretProvider,
        ILogger<DedicatedTotpSecretEncryptor> logger)
    {
        _secretProvider = secretProvider;
        _logger = logger;
    }

    public string Encrypt(string plaintext)
    {
        var (encKey, macKey) = DeriveCurrentKeys();

        byte[] iv = RandomNumberGenerator.GetBytes(16);
        byte[] ciphertext;

        using (var aes = Aes.Create())
        {
            aes.Key = encKey;
            aes.IV = iv;
            using var encryptor = aes.CreateEncryptor();
            byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
            ciphertext = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        }

        // Encrypt-then-MAC: HMAC over IV + ciphertext
        byte[] mac = ComputeMac(macKey, iv, ciphertext);

        return $"{CurrentVersion}:{Convert.ToBase64String(iv)}.{Convert.ToBase64String(ciphertext)}.{Convert.ToBase64String(mac)}";
    }

    public string Decrypt(string ciphertext)
    {
        if (IsLegacyEncrypted(ciphertext))
            return DecryptLegacy(ciphertext);

        return DecryptVersioned(ciphertext);
    }

    public bool IsLegacyEncrypted(string ciphertext)
    {
        // Legacy format: {Base64}.{Base64} (no version prefix)
        // Versioned format: v1:{Base64}.{Base64}.{Base64}
        return !ciphertext.StartsWith("v1:", StringComparison.Ordinal);
    }

    private string DecryptVersioned(string ciphertext)
    {
        // Format: v1:{Base64(IV)}.{Base64(Ciphertext)}.{Base64(HMAC)}
        string payload = ciphertext[3..]; // strip "v1:"
        var parts = payload.Split('.');
        if (parts.Length != 3)
            throw new CryptographicException("Invalid TOTP ciphertext format.");

        byte[] iv = Convert.FromBase64String(parts[0]);
        byte[] encrypted = Convert.FromBase64String(parts[1]);
        byte[] storedMac = Convert.FromBase64String(parts[2]);

        var (encKey, macKey) = DeriveCurrentKeys();

        // Verify MAC before decryption (Encrypt-then-MAC)
        byte[] computedMac = ComputeMac(macKey, iv, encrypted);
        if (!CryptographicOperations.FixedTimeEquals(storedMac, computedMac))
            throw new CryptographicException("TOTP secret MAC verification failed — key mismatch or data tampering.");

        using var aes = Aes.Create();
        aes.Key = encKey;
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        byte[] plainBytes = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// Decrypts a legacy ciphertext that was encrypted with the JWT-derived key.
    /// Format: {Base64(IV)}.{Base64(Ciphertext)} — no MAC, SHA256(JWT key) as AES key.
    /// </summary>
    private string DecryptLegacy(string ciphertext)
    {
        _logger.LogWarning(
            "Decrypting TOTP secret using legacy JWT-derived key. " +
            "This credential should be re-encrypted with the dedicated TOTP key on next verification.");

        string jwtKey = _secretProvider.GetSecret(JwtKeyEnvVar) ?? DevFallbackKey;
        byte[] keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(jwtKey));

        var parts = ciphertext.Split('.');
        if (parts.Length != 2)
            throw new CryptographicException("Invalid legacy TOTP ciphertext format.");

        byte[] iv = Convert.FromBase64String(parts[0]);
        byte[] encrypted = Convert.FromBase64String(parts[1]);

        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        byte[] plainBytes = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private (byte[] EncKey, byte[] MacKey) DeriveCurrentKeys()
    {
        string rawKey = _secretProvider.GetSecret(TotpKeyEnvVar)
            ?? _secretProvider.GetSecret(JwtKeyEnvVar)
            ?? DevFallbackKey;

        if (rawKey == DevFallbackKey)
        {
            _logger.LogWarning(
                "TOTP encryption using development fallback key. " +
                "Set {EnvVar} for production deployments.", TotpKeyEnvVar);
        }

        byte[] ikm = Encoding.UTF8.GetBytes(rawKey);
        byte[] encKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, info: EncKeyInfo);
        byte[] macKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, info: MacKeyInfo);
        return (encKey, macKey);
    }

    private static byte[] ComputeMac(byte[] macKey, byte[] iv, byte[] ciphertext)
    {
        // HMAC over IV || ciphertext
        using var hmac = new HMACSHA256(macKey);
        hmac.TransformBlock(iv, 0, iv.Length, null, 0);
        hmac.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
        return hmac.Hash!;
    }
}

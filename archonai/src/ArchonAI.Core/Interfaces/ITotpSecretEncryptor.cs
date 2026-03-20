namespace ArchonAI.Core.Interfaces;

/// <summary>
/// Encrypts and decrypts TOTP secrets at rest using a dedicated key
/// that is independent of JWT signing-key lifecycle.
///
/// Implementations must:
/// - Use AES-256-GCM (or CBC with HMAC) for authenticated encryption.
/// - Prefix ciphertext with a key version tag so that key rotation does
///   not break previously encrypted secrets.
/// - Support a migration path from legacy (JWT-derived) encryption.
/// </summary>
public interface ITotpSecretEncryptor
{
    /// <summary>
    /// Encrypts a plaintext TOTP secret. The returned ciphertext includes
    /// a key-version prefix so the correct key can be selected at decryption time.
    /// </summary>
    string Encrypt(string plaintext);

    /// <summary>
    /// Decrypts a TOTP secret. Automatically detects the key version from
    /// the ciphertext prefix and selects the correct key.
    /// Legacy ciphertexts (no version prefix) are decrypted using the
    /// JWT-derived fallback key, then transparently re-encrypted are NOT
    /// performed here — the caller must handle re-encryption if desired.
    /// </summary>
    string Decrypt(string ciphertext);

    /// <summary>
    /// Returns true if the ciphertext was encrypted with a legacy
    /// (JWT-derived) key and should be re-encrypted with the current
    /// dedicated key on next successful verification.
    /// </summary>
    bool IsLegacyEncrypted(string ciphertext);
}

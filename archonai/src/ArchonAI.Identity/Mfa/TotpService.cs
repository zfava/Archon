using System.Security.Cryptography;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using Microsoft.Extensions.Logging;
using OtpNet;

namespace ArchonAI.Identity.Mfa;

public sealed class TotpService
{
    private readonly IMfaStore _store;
    private readonly ITotpSecretEncryptor _encryptor;
    private readonly ILogger<TotpService> _logger;
    private const string Issuer = "ArchonAI";
    private const int RecoveryCodeCount = 10;

    public TotpService(IMfaStore store, ITotpSecretEncryptor encryptor, ILogger<TotpService> logger)
    {
        _store = store;
        _encryptor = encryptor;
        _logger = logger;
    }

    /// <summary>
    /// Generates a new TOTP setup. Returns the otpauth:// URI for QR code and plaintext recovery codes.
    /// The credential is stored as unverified until VerifySetupAsync confirms the first code.
    /// </summary>
    public async Task<(string OtpAuthUri, string[] RecoveryCodes)?> GenerateSetupAsync(
        Guid userId, string userEmail, CancellationToken ct = default)
    {
        var existing = await _store.GetTotpCredentialAsync(userId, ct);
        if (existing?.IsVerified == true)
        {
            _logger.LogWarning("TOTP setup rejected: already enrolled for user {UserId}", userId);
            return null;
        }

        // Remove any unverified previous attempt
        if (existing is not null)
            await _store.DeleteTotpCredentialAsync(userId, ct);

        // Generate secret
        byte[] secretBytes = RandomNumberGenerator.GetBytes(20);
        string base32Secret = Base32Encoding.ToString(secretBytes);
        string encryptedSecret = _encryptor.Encrypt(base32Secret);

        var credential = new TotpCredential(
            Id: Guid.NewGuid(),
            UserId: userId,
            EncryptedSecret: encryptedSecret,
            IsVerified: false,
            CreatedAtUtc: DateTimeOffset.UtcNow);
        await _store.CreateTotpCredentialAsync(credential, ct);

        // Generate recovery codes
        var (codes, hashes) = GenerateRecoveryCodes(userId);
        await _store.DeleteAllRecoveryCodesAsync(userId, ct);
        await _store.CreateRecoveryCodesAsync(hashes, ct);

        string otpAuthUri = $"otpauth://totp/{Issuer}:{Uri.EscapeDataString(userEmail)}" +
                           $"?secret={base32Secret}&issuer={Issuer}&algorithm=SHA1&digits=6&period=30";

        _logger.LogInformation("TOTP setup initiated for user {UserId}", userId);
        return (otpAuthUri, codes);
    }

    /// <summary>
    /// Verifies the first TOTP code to confirm enrollment. Makes the credential active.
    /// </summary>
    public async Task<bool> VerifySetupAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var credential = await _store.GetTotpCredentialAsync(userId, ct);
        if (credential is null || credential.IsVerified)
            return false;

        if (!ValidateCode(credential, code))
            return false;

        credential = await MigrateLegacySecretIfNeededAsync(credential, ct);
        await _store.UpdateTotpCredentialAsync(credential with { IsVerified = true }, ct);
        _logger.LogInformation("TOTP enrollment verified for user {UserId}", userId);
        return true;
    }

    /// <summary>
    /// Validates a TOTP code for an enrolled user (login challenge).
    /// </summary>
    public async Task<bool> VerifyAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var credential = await _store.GetTotpCredentialAsync(userId, ct);
        if (credential is null || !credential.IsVerified)
            return false;

        bool valid = ValidateCode(credential, code);
        if (valid)
            _ = await MigrateLegacySecretIfNeededAsync(credential, ct);
        _logger.LogInformation("TOTP verification {Result} for user {UserId}", valid ? "succeeded" : "failed", userId);
        return valid;
    }

    /// <summary>
    /// Disables TOTP for a user. Requires a valid current code to prevent lockout attacks.
    /// </summary>
    public async Task<bool> DisableAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var credential = await _store.GetTotpCredentialAsync(userId, ct);
        if (credential is null || !credential.IsVerified)
            return false;

        if (!ValidateCode(credential, code))
        {
            _logger.LogWarning("TOTP disable rejected: invalid code for user {UserId}", userId);
            return false;
        }

        await _store.DeleteTotpCredentialAsync(userId, ct);
        await _store.DeleteAllRecoveryCodesAsync(userId, ct);
        _logger.LogInformation("TOTP disabled for user {UserId}", userId);
        return true;
    }

    /// <summary>Checks if user has verified TOTP enrollment.</summary>
    public async Task<bool> IsEnabledAsync(Guid userId, CancellationToken ct = default)
    {
        var credential = await _store.GetTotpCredentialAsync(userId, ct);
        return credential?.IsVerified == true;
    }

    private bool ValidateCode(TotpCredential credential, string code)
    {
        string base32Secret = _encryptor.Decrypt(credential.EncryptedSecret);
        byte[] secretBytes = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(secretBytes, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
    }

    /// <summary>
    /// If the credential was encrypted with the legacy JWT-derived key,
    /// re-encrypts it with the dedicated TOTP key. Call after successful verification.
    /// Returns the credential with the updated EncryptedSecret (or the original if no migration needed).
    /// </summary>
    private async Task<TotpCredential> MigrateLegacySecretIfNeededAsync(TotpCredential credential, CancellationToken ct)
    {
        if (!_encryptor.IsLegacyEncrypted(credential.EncryptedSecret))
            return credential;

        string plainSecret = _encryptor.Decrypt(credential.EncryptedSecret);
        string reEncrypted = _encryptor.Encrypt(plainSecret);
        var migrated = credential with { EncryptedSecret = reEncrypted };
        await _store.UpdateTotpCredentialAsync(migrated, ct);
        _logger.LogInformation(
            "Migrated TOTP secret from legacy JWT-derived encryption to dedicated key for user {UserId}",
            credential.UserId);
        return migrated;
    }

    private (string[] PlainCodes, List<MfaRecoveryCode> HashedCodes) GenerateRecoveryCodes(Guid userId)
    {
        var plainCodes = new string[RecoveryCodeCount];
        var hashedCodes = new List<MfaRecoveryCode>(RecoveryCodeCount);

        for (int i = 0; i < RecoveryCodeCount; i++)
        {
            string code = GenerateRecoveryCode();
            plainCodes[i] = code;
            hashedCodes.Add(new MfaRecoveryCode(
                Id: Guid.NewGuid(),
                UserId: userId,
                CodeHash: HashRecoveryCode(code),
                IsUsed: false,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                UsedAtUtc: null));
        }

        return (plainCodes, hashedCodes);
    }

    private static string GenerateRecoveryCode()
    {
        // 8-character alphanumeric code in format XXXX-XXXX
        byte[] bytes = RandomNumberGenerator.GetBytes(5);
        string hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{hex[..4]}-{hex[4..8]}";
    }

    internal static string HashRecoveryCode(string code)
    {
        // Use PBKDF2 for recovery codes (bcrypt alternative without external dep)
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            System.Text.Encoding.UTF8.GetBytes(code.Replace("-", "").ToLowerInvariant()),
            salt, iterations: 50_000, HashAlgorithmName.SHA256, outputLength: 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    internal static bool VerifyRecoveryCode(string code, string storedHash)
    {
        var parts = storedHash.Split('.');
        if (parts.Length != 2) return false;
        byte[] salt = Convert.FromBase64String(parts[0]);
        byte[] expectedHash = Convert.FromBase64String(parts[1]);
        byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
            System.Text.Encoding.UTF8.GetBytes(code.Replace("-", "").ToLowerInvariant()),
            salt, iterations: 50_000, HashAlgorithmName.SHA256, outputLength: 32);
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }

}

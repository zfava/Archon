using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresMfaStore : IMfaStore
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresMfaStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private string TotpTable => $"{_schema}.totp_credentials";
    private string WebAuthnTable => $"{_schema}.webauthn_credentials";
    private string RecoveryCodesTable => $"{_schema}.mfa_recovery_codes";
    private string ChallengesTable => $"{_schema}.mfa_challenges";
    private string PoliciesTable => $"{_schema}.mfa_policies";

    public PostgresMfaStore(IOptions<PersistenceOptions> options, ILogger<PostgresMfaStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened ?? throw new ArgumentNullException(nameof(options), "Persistence connection string is not configured.");
        _schema = options.Value.Schema;
        _logger = logger;
    }

    // ── TOTP ────────────────────────────────────────────────────

    public async Task<TotpCredential?> GetTotpCredentialAsync(Guid userId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, user_id, encrypted_secret, is_verified, created_at_utc
            FROM {TotpTable}
            WHERE user_id = @userId;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new TotpCredential(
                reader.GetGuid(reader.GetOrdinal("id")),
                reader.GetGuid(reader.GetOrdinal("user_id")),
                reader.GetString(reader.GetOrdinal("encrypted_secret")),
                reader.GetBoolean(reader.GetOrdinal("is_verified")),
                new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at_utc")), TimeSpan.Zero));
        }

        return null;
    }

    public async Task CreateTotpCredentialAsync(TotpCredential credential, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            INSERT INTO {TotpTable} (id, user_id, encrypted_secret, is_verified, created_at_utc)
            VALUES (@id, @userId, @encryptedSecret, @isVerified, @createdAtUtc);
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", credential.Id);
        cmd.Parameters.AddWithValue("userId", credential.UserId);
        cmd.Parameters.AddWithValue("encryptedSecret", credential.EncryptedSecret);
        cmd.Parameters.AddWithValue("isVerified", credential.IsVerified);
        cmd.Parameters.AddWithValue("createdAtUtc", credential.CreatedAtUtc.UtcDateTime);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateTotpCredentialAsync(TotpCredential credential, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            UPDATE {TotpTable}
            SET encrypted_secret = @encryptedSecret, is_verified = @isVerified
            WHERE id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", credential.Id);
        cmd.Parameters.AddWithValue("encryptedSecret", credential.EncryptedSecret);
        cmd.Parameters.AddWithValue("isVerified", credential.IsVerified);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteTotpCredentialAsync(Guid userId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"DELETE FROM {TotpTable} WHERE user_id = @userId;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("userId", userId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── WebAuthn ────────────────────────────────────────────────

    public async Task<IReadOnlyList<WebAuthnCredential>> GetWebAuthnCredentialsAsync(Guid userId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, user_id, credential_id, public_key, sign_count, display_name, created_at_utc, last_used_at_utc
            FROM {WebAuthnTable}
            WHERE user_id = @userId;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<WebAuthnCredential>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadWebAuthnCredential(reader));
        }

        return results;
    }

    public async Task<WebAuthnCredential?> GetWebAuthnCredentialByIdAsync(byte[] credentialId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, user_id, credential_id, public_key, sign_count, display_name, created_at_utc, last_used_at_utc
            FROM {WebAuthnTable}
            WHERE credential_id = @credentialId;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("credentialId", credentialId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadWebAuthnCredential(reader);
        }

        return null;
    }

    public async Task CreateWebAuthnCredentialAsync(WebAuthnCredential credential, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            INSERT INTO {WebAuthnTable} (id, user_id, credential_id, public_key, sign_count, display_name, created_at_utc, last_used_at_utc)
            VALUES (@id, @userId, @credentialId, @publicKey, @signCount, @displayName, @createdAtUtc, @lastUsedAtUtc);
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", credential.Id);
        cmd.Parameters.AddWithValue("userId", credential.UserId);
        cmd.Parameters.AddWithValue("credentialId", credential.CredentialId);
        cmd.Parameters.AddWithValue("publicKey", credential.PublicKey);
        cmd.Parameters.AddWithValue("signCount", (int)credential.SignCount);
        cmd.Parameters.AddWithValue("displayName", credential.DisplayName);
        cmd.Parameters.AddWithValue("createdAtUtc", credential.CreatedAtUtc.UtcDateTime);
        cmd.Parameters.AddWithValue("lastUsedAtUtc", (object?)credential.LastUsedAtUtc?.UtcDateTime ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateWebAuthnCredentialAsync(WebAuthnCredential credential, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            UPDATE {WebAuthnTable}
            SET sign_count = @signCount, display_name = @displayName, last_used_at_utc = @lastUsedAtUtc
            WHERE id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", credential.Id);
        cmd.Parameters.AddWithValue("signCount", (int)credential.SignCount);
        cmd.Parameters.AddWithValue("displayName", credential.DisplayName);
        cmd.Parameters.AddWithValue("lastUsedAtUtc", (object?)credential.LastUsedAtUtc?.UtcDateTime ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteWebAuthnCredentialAsync(Guid credentialId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"DELETE FROM {WebAuthnTable} WHERE id = @id;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", credentialId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Recovery codes ──────────────────────────────────────────

    public async Task<IReadOnlyList<MfaRecoveryCode>> GetRecoveryCodesAsync(Guid userId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, user_id, code_hash, is_used, created_at_utc, used_at_utc
            FROM {RecoveryCodesTable}
            WHERE user_id = @userId;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<MfaRecoveryCode>();
        while (await reader.ReadAsync(ct))
        {
            var usedAtOrdinal = reader.GetOrdinal("used_at_utc");
            results.Add(new MfaRecoveryCode(
                reader.GetGuid(reader.GetOrdinal("id")),
                reader.GetGuid(reader.GetOrdinal("user_id")),
                reader.GetString(reader.GetOrdinal("code_hash")),
                reader.GetBoolean(reader.GetOrdinal("is_used")),
                new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at_utc")), TimeSpan.Zero),
                reader.IsDBNull(usedAtOrdinal) ? null : new DateTimeOffset(reader.GetFieldValue<DateTime>(usedAtOrdinal), TimeSpan.Zero)));
        }

        return results;
    }

    public async Task CreateRecoveryCodesAsync(IEnumerable<MfaRecoveryCode> codes, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var batch = new NpgsqlBatch(conn);

        foreach (var code in codes)
        {
            var cmd = new NpgsqlBatchCommand($"""
                INSERT INTO {RecoveryCodesTable} (id, user_id, code_hash, is_used, created_at_utc, used_at_utc)
                VALUES (@id, @userId, @codeHash, @isUsed, @createdAtUtc, @usedAtUtc);
                """);
            cmd.Parameters.AddWithValue("id", code.Id);
            cmd.Parameters.AddWithValue("userId", code.UserId);
            cmd.Parameters.AddWithValue("codeHash", code.CodeHash);
            cmd.Parameters.AddWithValue("isUsed", code.IsUsed);
            cmd.Parameters.AddWithValue("createdAtUtc", code.CreatedAtUtc.UtcDateTime);
            cmd.Parameters.AddWithValue("usedAtUtc", (object?)code.UsedAtUtc?.UtcDateTime ?? DBNull.Value);
            batch.BatchCommands.Add(cmd);
        }

        await batch.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkRecoveryCodeUsedAsync(Guid codeId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            UPDATE {RecoveryCodesTable}
            SET is_used = true, used_at_utc = @usedAtUtc
            WHERE id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", codeId);
        cmd.Parameters.AddWithValue("usedAtUtc", DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAllRecoveryCodesAsync(Guid userId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"DELETE FROM {RecoveryCodesTable} WHERE user_id = @userId;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("userId", userId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── MFA challenges ──────────────────────────────────────────

    public async Task CreateMfaChallengeAsync(MfaChallenge challenge, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            INSERT INTO {ChallengesTable} (id, user_id, token_hash, allowed_methods, expires_at_utc, created_at_utc, is_used)
            VALUES (@id, @userId, @tokenHash, @allowedMethods::jsonb, @expiresAtUtc, @createdAtUtc, @isUsed);
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", challenge.Id);
        cmd.Parameters.AddWithValue("userId", challenge.UserId);
        cmd.Parameters.AddWithValue("tokenHash", challenge.TokenHash);
        cmd.Parameters.AddWithValue("allowedMethods", JsonSerializer.Serialize(challenge.AllowedMethods));
        cmd.Parameters.AddWithValue("expiresAtUtc", challenge.ExpiresAtUtc.UtcDateTime);
        cmd.Parameters.AddWithValue("createdAtUtc", challenge.CreatedAtUtc.UtcDateTime);
        cmd.Parameters.AddWithValue("isUsed", challenge.IsUsed);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<MfaChallenge?> GetMfaChallengeByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, user_id, token_hash, allowed_methods, expires_at_utc, created_at_utc, is_used
            FROM {ChallengesTable}
            WHERE token_hash = @tokenHash AND is_used = false AND expires_at_utc > now();
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tokenHash", tokenHash);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var methodsJson = reader.GetString(reader.GetOrdinal("allowed_methods"));
            var methods = JsonSerializer.Deserialize<string[]>(methodsJson) ?? Array.Empty<string>();

            return new MfaChallenge(
                reader.GetGuid(reader.GetOrdinal("id")),
                reader.GetGuid(reader.GetOrdinal("user_id")),
                reader.GetString(reader.GetOrdinal("token_hash")),
                methods,
                new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("expires_at_utc")), TimeSpan.Zero),
                new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at_utc")), TimeSpan.Zero),
                reader.GetBoolean(reader.GetOrdinal("is_used")));
        }

        return null;
    }

    public async Task MarkMfaChallengeUsedAsync(Guid challengeId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"UPDATE {ChallengesTable} SET is_used = true WHERE id = @id;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", challengeId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── MFA policy ──────────────────────────────────────────────

    public async Task<MfaPolicy?> GetMfaPolicyAsync(Guid organizationId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT organization_id, mode, updated_at_utc
            FROM {PoliciesTable}
            WHERE organization_id = @organizationId;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("organizationId", organizationId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new MfaPolicy(
                reader.GetGuid(reader.GetOrdinal("organization_id")),
                (MfaPolicyMode)reader.GetInt32(reader.GetOrdinal("mode")),
                new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("updated_at_utc")), TimeSpan.Zero));
        }

        return null;
    }

    public async Task UpsertMfaPolicyAsync(MfaPolicy policy, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            INSERT INTO {PoliciesTable} (organization_id, mode, updated_at_utc)
            VALUES (@organizationId, @mode, @updatedAtUtc)
            ON CONFLICT (organization_id)
            DO UPDATE SET mode = @mode, updated_at_utc = @updatedAtUtc;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("organizationId", policy.OrganizationId);
        cmd.Parameters.AddWithValue("mode", (int)policy.Mode);
        cmd.Parameters.AddWithValue("updatedAtUtc", policy.UpdatedAtUtc.UtcDateTime);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static WebAuthnCredential ReadWebAuthnCredential(NpgsqlDataReader reader)
    {
        var lastUsedOrdinal = reader.GetOrdinal("last_used_at_utc");
        return new WebAuthnCredential(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetGuid(reader.GetOrdinal("user_id")),
            (byte[])reader[reader.GetOrdinal("credential_id")],
            (byte[])reader[reader.GetOrdinal("public_key")],
            (uint)reader.GetInt32(reader.GetOrdinal("sign_count")),
            reader.GetString(reader.GetOrdinal("display_name")),
            new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at_utc")), TimeSpan.Zero),
            reader.IsDBNull(lastUsedOrdinal) ? null : new DateTimeOffset(reader.GetFieldValue<DateTime>(lastUsedOrdinal), TimeSpan.Zero));
    }

    private Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return Task.CompletedTask;
        _initialized = true;
        return Task.CompletedTask;
    }
}

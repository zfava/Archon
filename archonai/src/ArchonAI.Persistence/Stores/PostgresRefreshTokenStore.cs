using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresRefreshTokenStore : IRefreshTokenStore
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresRefreshTokenStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private string TableName => $"{_schema}.refresh_tokens";

    public PostgresRefreshTokenStore(IOptions<PersistenceOptions> options, ILogger<PostgresRefreshTokenStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened ?? throw new InvalidOperationException("Persistence connection string is not configured.");
        _schema = options.Value.Schema;
        _logger = logger;
    }

    public async Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            INSERT INTO {TableName}
            (id, user_id, token_hash, expires_at_utc, created_at_utc, is_revoked)
            VALUES
            (@id, @userId, @tokenHash, @expiresAtUtc, @createdAtUtc, @isRevoked);
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", token.Id);
        cmd.Parameters.AddWithValue("userId", token.UserId);
        cmd.Parameters.AddWithValue("tokenHash", token.TokenHash);
        cmd.Parameters.AddWithValue("expiresAtUtc", token.ExpiresAtUtc.UtcDateTime);
        cmd.Parameters.AddWithValue("createdAtUtc", token.CreatedAtUtc.UtcDateTime);
        cmd.Parameters.AddWithValue("isRevoked", token.IsRevoked);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("Refresh token {TokenId} created for user {UserId}", token.Id, token.UserId);

        return token;
    }

    public async Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, user_id, token_hash, expires_at_utc, created_at_utc, is_revoked
            FROM {TableName}
            WHERE token_hash = @tokenHash AND is_revoked = false AND expires_at_utc > now();
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tokenHash", tokenHash);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadRefreshToken(reader);
        }

        return null;
    }

    public async Task RevokeAsync(Guid tokenId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            UPDATE {TableName}
            SET is_revoked = true
            WHERE id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", tokenId);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("Refresh token {TokenId} revoked", tokenId);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            UPDATE {TableName}
            SET is_revoked = true
            WHERE user_id = @userId AND is_revoked = false;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("userId", userId);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("All refresh tokens revoked for user {UserId}", userId);
    }

    private static RefreshToken ReadRefreshToken(NpgsqlDataReader reader)
    {
        return new RefreshToken(
            Id: reader.GetGuid(reader.GetOrdinal("id")),
            UserId: reader.GetGuid(reader.GetOrdinal("user_id")),
            TokenHash: reader.GetString(reader.GetOrdinal("token_hash")),
            ExpiresAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("expires_at_utc")), TimeSpan.Zero),
            CreatedAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at_utc")), TimeSpan.Zero),
            IsRevoked: reader.GetBoolean(reader.GetOrdinal("is_revoked")));
    }

    private Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return Task.CompletedTask;
        _initialized = true;
        return Task.CompletedTask;
    }
}

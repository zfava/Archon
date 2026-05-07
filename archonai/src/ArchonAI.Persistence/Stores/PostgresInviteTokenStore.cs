using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresInviteTokenStore : IInviteTokenStore
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresInviteTokenStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private string TableName => $"{_schema}.invite_tokens";

    public PostgresInviteTokenStore(IOptions<PersistenceOptions> options, ILogger<PostgresInviteTokenStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened ?? throw new InvalidOperationException("Persistence connection string is not configured.");
        _schema = options.Value.Schema;
        _logger = logger;
    }

    public async Task<InviteToken> CreateAsync(InviteToken invite, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            INSERT INTO {TableName}
            (id, organization_id, email, role, token_hash, expires_at_utc, created_at_utc, is_accepted)
            VALUES
            (@id, @organizationId, @email, @role, @tokenHash, @expiresAtUtc, @createdAtUtc, @isAccepted);
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", invite.Id);
        cmd.Parameters.AddWithValue("organizationId", invite.OrganizationId);
        cmd.Parameters.AddWithValue("email", invite.Email);
        cmd.Parameters.AddWithValue("role", invite.Role);
        cmd.Parameters.AddWithValue("tokenHash", invite.TokenHash);
        cmd.Parameters.AddWithValue("expiresAtUtc", invite.ExpiresAtUtc.UtcDateTime);
        cmd.Parameters.AddWithValue("createdAtUtc", invite.CreatedAtUtc.UtcDateTime);
        cmd.Parameters.AddWithValue("isAccepted", invite.IsAccepted);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("Invite token {InviteId} created for email {Email} in org {OrgId}", invite.Id, invite.Email, invite.OrganizationId);

        return invite;
    }

    public async Task<InviteToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, organization_id, email, role, token_hash, expires_at_utc, created_at_utc, is_accepted
            FROM {TableName}
            WHERE token_hash = @tokenHash AND is_accepted = false AND expires_at_utc > now();
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tokenHash", tokenHash);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadInviteToken(reader);
        }

        return null;
    }

    public async Task AcceptAsync(Guid inviteId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            UPDATE {TableName}
            SET is_accepted = true
            WHERE id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", inviteId);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("Invite token {InviteId} accepted", inviteId);
    }

    private static InviteToken ReadInviteToken(NpgsqlDataReader reader)
    {
        return new InviteToken(
            Id: reader.GetGuid(reader.GetOrdinal("id")),
            OrganizationId: reader.GetGuid(reader.GetOrdinal("organization_id")),
            Email: reader.GetString(reader.GetOrdinal("email")),
            Role: reader.GetString(reader.GetOrdinal("role")),
            TokenHash: reader.GetString(reader.GetOrdinal("token_hash")),
            ExpiresAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("expires_at_utc")), TimeSpan.Zero),
            CreatedAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at_utc")), TimeSpan.Zero),
            IsAccepted: reader.GetBoolean(reader.GetOrdinal("is_accepted")));
    }

    private Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return Task.CompletedTask;
        _initialized = true;
        return Task.CompletedTask;
    }
}

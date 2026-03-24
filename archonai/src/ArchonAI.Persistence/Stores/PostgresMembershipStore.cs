using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresMembershipStore : IMembershipStore
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresMembershipStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private string TableName => $"{_schema}.memberships";

    public PostgresMembershipStore(IOptions<PersistenceOptions> options, ILogger<PostgresMembershipStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened ?? throw new InvalidOperationException("Persistence connection string is not configured.");
        _schema = options.Value.Schema;
        _logger = logger;
    }

    public async Task<Membership> CreateAsync(Membership membership, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            INSERT INTO {TableName}
            (id, user_id, organization_id, role, joined_at_utc)
            VALUES
            (@id, @userId, @organizationId, @role, @joinedAtUtc);
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", membership.Id);
        cmd.Parameters.AddWithValue("userId", membership.UserId);
        cmd.Parameters.AddWithValue("organizationId", membership.OrganizationId);
        cmd.Parameters.AddWithValue("role", membership.Role);
        cmd.Parameters.AddWithValue("joinedAtUtc", membership.JoinedAtUtc.UtcDateTime);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("Membership {MembershipId} created for user {UserId} in org {OrgId}", membership.Id, membership.UserId, membership.OrganizationId);

        return membership;
    }

    public async Task<Membership?> GetAsync(Guid userId, Guid orgId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, user_id, organization_id, role, joined_at_utc
            FROM {TableName}
            WHERE user_id = @userId AND organization_id = @orgId;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("orgId", orgId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadMembership(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<Membership>> ListByUserAsync(Guid userId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, user_id, organization_id, role, joined_at_utc
            FROM {TableName}
            WHERE user_id = @userId
            ORDER BY joined_at_utc ASC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var memberships = new List<Membership>();
        while (await reader.ReadAsync(ct))
        {
            memberships.Add(ReadMembership(reader));
        }

        return memberships;
    }

    public async Task<IReadOnlyList<Membership>> ListByOrganizationAsync(Guid orgId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, user_id, organization_id, role, joined_at_utc
            FROM {TableName}
            WHERE organization_id = @orgId
            ORDER BY joined_at_utc ASC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("orgId", orgId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var memberships = new List<Membership>();
        while (await reader.ReadAsync(ct))
        {
            memberships.Add(ReadMembership(reader));
        }

        return memberships;
    }

    public async Task RemoveAsync(Guid userId, Guid orgId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            DELETE FROM {TableName}
            WHERE user_id = @userId AND organization_id = @orgId;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("orgId", orgId);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("Membership removed for user {UserId} in org {OrgId}", userId, orgId);
    }

    private static Membership ReadMembership(NpgsqlDataReader reader)
    {
        return new Membership(
            Id: reader.GetGuid(reader.GetOrdinal("id")),
            UserId: reader.GetGuid(reader.GetOrdinal("user_id")),
            OrganizationId: reader.GetGuid(reader.GetOrdinal("organization_id")),
            Role: reader.GetString(reader.GetOrdinal("role")),
            JoinedAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("joined_at_utc")), TimeSpan.Zero));
    }

    private Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return Task.CompletedTask;
        _initialized = true;
        return Task.CompletedTask;
    }
}

using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresUserStore : IUserStore
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresUserStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private string TableName => $"{_schema}.users";

    public PostgresUserStore(IOptions<PersistenceOptions> options, ILogger<PostgresUserStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened ?? throw new InvalidOperationException("Persistence connection string is not configured.");
        _schema = options.Value.Schema;
        _logger = logger;
    }

    public async Task<UserIdentity?> GetByIdAsync(Guid userId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, email, display_name, password_hash, organization_id, role, is_active, created_at_utc, last_login_at_utc
            FROM {TableName}
            WHERE id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", userId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadUser(reader);
        }

        return null;
    }

    public async Task<UserIdentity?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, email, display_name, password_hash, organization_id, role, is_active, created_at_utc, last_login_at_utc
            FROM {TableName}
            WHERE email = @email;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("email", email);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadUser(reader);
        }

        return null;
    }

    public async Task<UserIdentity> CreateAsync(UserIdentity user, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            INSERT INTO {TableName}
            (id, email, display_name, password_hash, organization_id, role, is_active, created_at_utc, last_login_at_utc)
            VALUES
            (@id, @email, @displayName, @passwordHash, @organizationId, @role, @isActive, @createdAtUtc, @lastLoginAtUtc);
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", user.Id);
        cmd.Parameters.AddWithValue("email", user.Email);
        cmd.Parameters.AddWithValue("displayName", user.DisplayName);
        cmd.Parameters.AddWithValue("passwordHash", user.PasswordHash);
        cmd.Parameters.AddWithValue("organizationId", user.OrganizationId);
        cmd.Parameters.AddWithValue("role", user.Role);
        cmd.Parameters.AddWithValue("isActive", user.IsActive);
        cmd.Parameters.AddWithValue("createdAtUtc", user.CreatedAtUtc.UtcDateTime);
        cmd.Parameters.AddWithValue("lastLoginAtUtc", (object?)user.LastLoginAtUtc?.UtcDateTime ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("User {UserId} created with email {Email}", user.Id, user.Email);

        return user;
    }

    public async Task<UserIdentity> UpdateAsync(UserIdentity user, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            UPDATE {TableName}
            SET email = @email, display_name = @displayName, password_hash = @passwordHash,
                organization_id = @organizationId, role = @role, is_active = @isActive, last_login_at_utc = @lastLoginAtUtc
            WHERE id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", user.Id);
        cmd.Parameters.AddWithValue("email", user.Email);
        cmd.Parameters.AddWithValue("displayName", user.DisplayName);
        cmd.Parameters.AddWithValue("passwordHash", user.PasswordHash);
        cmd.Parameters.AddWithValue("organizationId", user.OrganizationId);
        cmd.Parameters.AddWithValue("role", user.Role);
        cmd.Parameters.AddWithValue("isActive", user.IsActive);
        cmd.Parameters.AddWithValue("lastLoginAtUtc", (object?)user.LastLoginAtUtc?.UtcDateTime ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("User {UserId} updated", user.Id);

        return user;
    }

    public async Task<IReadOnlyList<UserIdentity>> ListByOrganizationAsync(Guid orgId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, email, display_name, password_hash, organization_id, role, is_active, created_at_utc, last_login_at_utc
            FROM {TableName}
            WHERE organization_id = @orgId
            ORDER BY created_at_utc ASC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("orgId", orgId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var users = new List<UserIdentity>();
        while (await reader.ReadAsync(ct))
        {
            users.Add(ReadUser(reader));
        }

        return users;
    }

    private static UserIdentity ReadUser(NpgsqlDataReader reader)
    {
        var lastLoginOrdinal = reader.GetOrdinal("last_login_at_utc");
        DateTimeOffset? lastLoginAtUtc = reader.IsDBNull(lastLoginOrdinal)
            ? null
            : new DateTimeOffset(reader.GetFieldValue<DateTime>(lastLoginOrdinal), TimeSpan.Zero);

        return new UserIdentity(
            Id: reader.GetGuid(reader.GetOrdinal("id")),
            Email: reader.GetString(reader.GetOrdinal("email")),
            DisplayName: reader.GetString(reader.GetOrdinal("display_name")),
            PasswordHash: reader.GetString(reader.GetOrdinal("password_hash")),
            OrganizationId: reader.GetGuid(reader.GetOrdinal("organization_id")),
            Role: reader.GetString(reader.GetOrdinal("role")),
            IsActive: reader.GetBoolean(reader.GetOrdinal("is_active")),
            CreatedAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at_utc")), TimeSpan.Zero),
            LastLoginAtUtc: lastLoginAtUtc);
    }

    private Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return Task.CompletedTask;
        _initialized = true;
        return Task.CompletedTask;
    }
}

using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresOrganizationStore : IOrganizationStore
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresOrganizationStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private string TableName => $"{_schema}.organizations";

    public PostgresOrganizationStore(IOptions<PersistenceOptions> options, ILogger<PostgresOrganizationStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened ?? throw new InvalidOperationException("Persistence connection string is not configured.");
        _schema = options.Value.Schema;
        _logger = logger;
    }

    public async Task<Organization?> GetByIdAsync(Guid orgId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, name, slug, is_active, created_at_utc
            FROM {TableName}
            WHERE id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", orgId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadOrganization(reader);
        }

        return null;
    }

    public async Task<Organization?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, name, slug, is_active, created_at_utc
            FROM {TableName}
            WHERE slug = @slug;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("slug", slug);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadOrganization(reader);
        }

        return null;
    }

    public async Task<Organization> CreateAsync(Organization org, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            INSERT INTO {TableName}
            (id, name, slug, is_active, created_at_utc)
            VALUES
            (@id, @name, @slug, @isActive, @createdAtUtc);
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", org.Id);
        cmd.Parameters.AddWithValue("name", org.Name);
        cmd.Parameters.AddWithValue("slug", org.Slug);
        cmd.Parameters.AddWithValue("isActive", org.IsActive);
        cmd.Parameters.AddWithValue("createdAtUtc", org.CreatedAtUtc.UtcDateTime);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("Organization {OrgId} created with slug {Slug}", org.Id, org.Slug);

        return org;
    }

    public async Task<IReadOnlyList<Organization>> ListAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, name, slug, is_active, created_at_utc
            FROM {TableName}
            ORDER BY created_at_utc ASC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var orgs = new List<Organization>();
        while (await reader.ReadAsync(ct))
        {
            orgs.Add(ReadOrganization(reader));
        }

        return orgs;
    }

    private static Organization ReadOrganization(NpgsqlDataReader reader)
    {
        return new Organization(
            Id: reader.GetGuid(reader.GetOrdinal("id")),
            Name: reader.GetString(reader.GetOrdinal("name")),
            Slug: reader.GetString(reader.GetOrdinal("slug")),
            IsActive: reader.GetBoolean(reader.GetOrdinal("is_active")),
            CreatedAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at_utc")), TimeSpan.Zero));
    }

    private Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return Task.CompletedTask;
        _initialized = true;
        return Task.CompletedTask;
    }
}

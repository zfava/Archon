using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresExternalIdentityLinkStore : IExternalIdentityLinkStore
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresExternalIdentityLinkStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private string TableName => $"{_schema}.external_identity_links";

    public PostgresExternalIdentityLinkStore(IOptions<PersistenceOptions> options, ILogger<PostgresExternalIdentityLinkStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened ?? throw new ArgumentNullException(nameof(options), "Persistence connection string is not configured.");
        _schema = options.Value.Schema;
        _logger = logger;
    }

    public async Task<ExternalIdentityLink?> GetByExternalSubjectAsync(string externalSubject, string externalIssuer, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, user_id, organization_id, provider_type, external_subject, external_issuer, external_email, created_at_utc, last_used_at_utc
            FROM {TableName}
            WHERE external_subject = @externalSubject AND external_issuer = @externalIssuer;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("externalSubject", externalSubject);
        cmd.Parameters.AddWithValue("externalIssuer", externalIssuer);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadLink(reader);
        }

        return null;
    }

    public async Task<ExternalIdentityLink?> GetByUserIdAsync(Guid userId, Guid orgId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, user_id, organization_id, provider_type, external_subject, external_issuer, external_email, created_at_utc, last_used_at_utc
            FROM {TableName}
            WHERE user_id = @userId AND organization_id = @orgId;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("orgId", orgId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadLink(reader);
        }

        return null;
    }

    public async Task<ExternalIdentityLink> CreateAsync(ExternalIdentityLink link, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            INSERT INTO {TableName} (id, user_id, organization_id, provider_type, external_subject, external_issuer, external_email, created_at_utc, last_used_at_utc)
            VALUES (@id, @userId, @organizationId, @providerType, @externalSubject, @externalIssuer, @externalEmail, @createdAtUtc, @lastUsedAtUtc);
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", link.Id);
        cmd.Parameters.AddWithValue("userId", link.UserId);
        cmd.Parameters.AddWithValue("organizationId", link.OrganizationId);
        cmd.Parameters.AddWithValue("providerType", (int)link.ProviderType);
        cmd.Parameters.AddWithValue("externalSubject", link.ExternalSubject);
        cmd.Parameters.AddWithValue("externalIssuer", link.ExternalIssuer);
        cmd.Parameters.AddWithValue("externalEmail", (object?)link.ExternalEmail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("createdAtUtc", link.CreatedAtUtc.UtcDateTime);
        cmd.Parameters.AddWithValue("lastUsedAtUtc", (object?)link.LastUsedAtUtc?.UtcDateTime ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);

        return link;
    }

    public async Task<ExternalIdentityLink> UpdateAsync(ExternalIdentityLink link, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            UPDATE {TableName}
            SET provider_type = @providerType, external_subject = @externalSubject, external_issuer = @externalIssuer,
                external_email = @externalEmail, last_used_at_utc = @lastUsedAtUtc
            WHERE id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", link.Id);
        cmd.Parameters.AddWithValue("providerType", (int)link.ProviderType);
        cmd.Parameters.AddWithValue("externalSubject", link.ExternalSubject);
        cmd.Parameters.AddWithValue("externalIssuer", link.ExternalIssuer);
        cmd.Parameters.AddWithValue("externalEmail", (object?)link.ExternalEmail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("lastUsedAtUtc", (object?)link.LastUsedAtUtc?.UtcDateTime ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);

        return link;
    }

    public async Task<IReadOnlyList<ExternalIdentityLink>> ListByOrganizationAsync(Guid orgId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, user_id, organization_id, provider_type, external_subject, external_issuer, external_email, created_at_utc, last_used_at_utc
            FROM {TableName}
            WHERE organization_id = @orgId;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("orgId", orgId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ExternalIdentityLink>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadLink(reader));
        }

        return results;
    }

    private static ExternalIdentityLink ReadLink(NpgsqlDataReader reader)
    {
        var emailOrdinal = reader.GetOrdinal("external_email");
        var lastUsedOrdinal = reader.GetOrdinal("last_used_at_utc");

        return new ExternalIdentityLink(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetGuid(reader.GetOrdinal("user_id")),
            reader.GetGuid(reader.GetOrdinal("organization_id")),
            (OidcProviderType)reader.GetInt32(reader.GetOrdinal("provider_type")),
            reader.GetString(reader.GetOrdinal("external_subject")),
            reader.GetString(reader.GetOrdinal("external_issuer")),
            reader.IsDBNull(emailOrdinal) ? null : reader.GetString(emailOrdinal),
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

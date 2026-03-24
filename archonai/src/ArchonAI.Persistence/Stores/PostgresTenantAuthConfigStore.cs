using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresTenantAuthConfigStore : ITenantAuthConfigStore
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresTenantAuthConfigStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private string TableName => $"{_schema}.tenant_auth_configs";

    public PostgresTenantAuthConfigStore(IOptions<PersistenceOptions> options, ILogger<PostgresTenantAuthConfigStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened ?? throw new ArgumentNullException(nameof(options), "Persistence connection string is not configured.");
        _schema = options.Value.Schema;
        _logger = logger;
    }

    public async Task<TenantAuthConfig?> GetByOrganizationIdAsync(Guid orgId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, organization_id, provider_type, authority, client_id, client_secret, domain, scopes, auto_provision, default_role, is_enabled, created_at_utc, updated_at_utc
            FROM {TableName}
            WHERE organization_id = @orgId;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("orgId", orgId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadConfig(reader);
        }

        return null;
    }

    public async Task<TenantAuthConfig?> GetByIdAsync(Guid configId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, organization_id, provider_type, authority, client_id, client_secret, domain, scopes, auto_provision, default_role, is_enabled, created_at_utc, updated_at_utc
            FROM {TableName}
            WHERE id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", configId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadConfig(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<TenantAuthConfig>> ListAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            SELECT id, organization_id, provider_type, authority, client_id, client_secret, domain, scopes, auto_provision, default_role, is_enabled, created_at_utc, updated_at_utc
            FROM {TableName};
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<TenantAuthConfig>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadConfig(reader));
        }

        return results;
    }

    public async Task<TenantAuthConfig> CreateAsync(TenantAuthConfig config, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            INSERT INTO {TableName} (id, organization_id, provider_type, authority, client_id, client_secret, domain, scopes, auto_provision, default_role, is_enabled, created_at_utc, updated_at_utc)
            VALUES (@id, @organizationId, @providerType, @authority, @clientId, @clientSecret, @domain, @scopes::jsonb, @autoProvision, @defaultRole, @isEnabled, @createdAtUtc, @updatedAtUtc);
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", config.Id);
        cmd.Parameters.AddWithValue("organizationId", config.OrganizationId);
        cmd.Parameters.AddWithValue("providerType", (int)config.ProviderType);
        cmd.Parameters.AddWithValue("authority", config.Authority);
        cmd.Parameters.AddWithValue("clientId", config.ClientId);
        cmd.Parameters.AddWithValue("clientSecret", config.ClientSecret);
        cmd.Parameters.AddWithValue("domain", (object?)config.Domain ?? DBNull.Value);
        cmd.Parameters.AddWithValue("scopes", JsonSerializer.Serialize(config.Scopes));
        cmd.Parameters.AddWithValue("autoProvision", config.AutoProvision);
        cmd.Parameters.AddWithValue("defaultRole", config.DefaultRole);
        cmd.Parameters.AddWithValue("isEnabled", config.IsEnabled);
        cmd.Parameters.AddWithValue("createdAtUtc", config.CreatedAtUtc.UtcDateTime);
        cmd.Parameters.AddWithValue("updatedAtUtc", (object?)config.UpdatedAtUtc?.UtcDateTime ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);

        return config;
    }

    public async Task<TenantAuthConfig> UpdateAsync(TenantAuthConfig config, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"""
            UPDATE {TableName}
            SET organization_id = @organizationId, provider_type = @providerType, authority = @authority,
                client_id = @clientId, client_secret = @clientSecret, domain = @domain,
                scopes = @scopes::jsonb, auto_provision = @autoProvision, default_role = @defaultRole,
                is_enabled = @isEnabled, updated_at_utc = @updatedAtUtc
            WHERE id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", config.Id);
        cmd.Parameters.AddWithValue("organizationId", config.OrganizationId);
        cmd.Parameters.AddWithValue("providerType", (int)config.ProviderType);
        cmd.Parameters.AddWithValue("authority", config.Authority);
        cmd.Parameters.AddWithValue("clientId", config.ClientId);
        cmd.Parameters.AddWithValue("clientSecret", config.ClientSecret);
        cmd.Parameters.AddWithValue("domain", (object?)config.Domain ?? DBNull.Value);
        cmd.Parameters.AddWithValue("scopes", JsonSerializer.Serialize(config.Scopes));
        cmd.Parameters.AddWithValue("autoProvision", config.AutoProvision);
        cmd.Parameters.AddWithValue("defaultRole", config.DefaultRole);
        cmd.Parameters.AddWithValue("isEnabled", config.IsEnabled);
        cmd.Parameters.AddWithValue("updatedAtUtc", (object?)config.UpdatedAtUtc?.UtcDateTime ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);

        return config;
    }

    public async Task DeleteAsync(Guid configId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"DELETE FROM {TableName} WHERE id = @id;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", configId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static TenantAuthConfig ReadConfig(NpgsqlDataReader reader)
    {
        var domainOrdinal = reader.GetOrdinal("domain");
        var updatedAtOrdinal = reader.GetOrdinal("updated_at_utc");
        var scopesJson = reader.GetString(reader.GetOrdinal("scopes"));
        var scopes = JsonSerializer.Deserialize<string[]>(scopesJson) ?? Array.Empty<string>();

        return new TenantAuthConfig(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetGuid(reader.GetOrdinal("organization_id")),
            (OidcProviderType)reader.GetInt32(reader.GetOrdinal("provider_type")),
            reader.GetString(reader.GetOrdinal("authority")),
            reader.GetString(reader.GetOrdinal("client_id")),
            reader.GetString(reader.GetOrdinal("client_secret")),
            reader.IsDBNull(domainOrdinal) ? null : reader.GetString(domainOrdinal),
            scopes,
            reader.GetBoolean(reader.GetOrdinal("auto_provision")),
            reader.GetString(reader.GetOrdinal("default_role")),
            reader.GetBoolean(reader.GetOrdinal("is_enabled")),
            new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at_utc")), TimeSpan.Zero),
            reader.IsDBNull(updatedAtOrdinal) ? null : new DateTimeOffset(reader.GetFieldValue<DateTime>(updatedAtOrdinal), TimeSpan.Zero));
    }

    private Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return Task.CompletedTask;
        _initialized = true;
        return Task.CompletedTask;
    }
}

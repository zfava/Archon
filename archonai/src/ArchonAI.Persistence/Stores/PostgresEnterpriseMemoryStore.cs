using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresEnterpriseMemoryStore : IEnterpriseMemoryService
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresEnterpriseMemoryStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private string TableName => $"{_schema}.enterprise_memory_records";

    public PostgresEnterpriseMemoryStore(
        IOptions<PersistenceOptions> options,
        ILogger<PostgresEnterpriseMemoryStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened
            ?? throw new ArgumentNullException(nameof(options), "ConnectionString is required.");
        _schema = options.Value.Schema;
        _logger = logger;
    }

    // ── Initialization ──────────────────────────────────────────────

    private Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return Task.CompletedTask;
        // Table creation is managed by DbUp migrations in ArchonAI.Migrations.
        // See Scripts/012_create_enterprise_memory.sql
        _initialized = true;
        return Task.CompletedTask;
    }

    // ── StoreAsync ──────────────────────────────────────────────────

    public async Task<EnterpriseMemoryRecord> StoreAsync(
        EnterpriseMemoryRecord record, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        // Auto-set 4-hour TTL for Session layer if no expiry specified
        var expiresAtUtc = record.ExpiresAtUtc;
        if (record.Layer == MemoryLayer.Session && expiresAtUtc is null)
        {
            expiresAtUtc = record.CreatedAtUtc.AddHours(4);
            record = record with { ExpiresAtUtc = expiresAtUtc };
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {TableName}
                (id, tenant_id, layer, category, subject, content, metadata, linked_entities,
                 tags, importance, created_by, created_at_utc, expires_at_utc)
            VALUES
                (@id, @tenantId, @layer, @category, @subject, @content, @metadata::jsonb,
                 @linkedEntities::jsonb, @tags::jsonb, @importance, @createdBy,
                 @createdAtUtc, @expiresAtUtc)
        ", conn);

        AddParameters(cmd, record);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("Memory record {RecordId} stored in layer {Layer}.", record.Id, record.Layer);
        return record;
    }

    // ── GetAsync ────────────────────────────────────────────────────

    public async Task<EnterpriseMemoryRecord?> GetAsync(
        Guid recordId, Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {TableName}
            WHERE id = @id AND tenant_id = @tenantId
              AND (expires_at_utc IS NULL OR expires_at_utc > @now)
        ", conn);
        cmd.Parameters.AddWithValue("id", recordId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRecord(reader) : null;
    }

    // ── QueryAsync ──────────────────────────────────────────────────

    public async Task<EnterpriseMemoryQueryResult> QueryAsync(
        Guid tenantId, MemoryLayer? layer = null, string? category = null,
        string? tag = null, int limit = 50, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var whereClauses = new List<string> { "tenant_id = @tenantId", "(expires_at_utc IS NULL OR expires_at_utc > @now)" };
        if (layer.HasValue) whereClauses.Add("layer = @layer");
        if (category is not null) whereClauses.Add("category = @category");
        if (tag is not null) whereClauses.Add("tags @> @tag::jsonb");

        var whereClause = string.Join(" AND ", whereClauses);

        // Count query with layer breakdown
        var countSql = $@"
            SELECT layer, COUNT(*) AS cnt
            FROM {TableName}
            WHERE {whereClause}
            GROUP BY layer
        ";

        await using var countCmd = new NpgsqlCommand(countSql, conn);
        AddFilterParameters(countCmd, tenantId, layer, category, tag);

        var layerCounts = new Dictionary<string, int>();
        int totalCount = 0;
        await using (var reader = await countCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var layerValue = (MemoryLayer)reader.GetInt32(reader.GetOrdinal("layer"));
                var count = reader.GetInt32(reader.GetOrdinal("cnt"));
                layerCounts[layerValue.ToString()] = count;
                totalCount += count;
            }
        }

        // Data query
        var dataSql = $@"
            SELECT * FROM {TableName}
            WHERE {whereClause}
            ORDER BY importance DESC, created_at_utc DESC
            LIMIT @limit
        ";

        await using var dataCmd = new NpgsqlCommand(dataSql, conn);
        AddFilterParameters(dataCmd, tenantId, layer, category, tag);
        dataCmd.Parameters.AddWithValue("limit", limit);

        var records = new List<EnterpriseMemoryRecord>();
        await using (var reader = await dataCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                records.Add(MapRecord(reader));
        }

        return new EnterpriseMemoryQueryResult(records, totalCount, layerCounts);
    }

    // ── GetEntityMemoryAsync ────────────────────────────────────────

    public async Task<EntityMemoryView> GetEntityMemoryAsync(
        Guid tenantId, string entityType, string entityId,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Use jsonb containment operator to find linked entities
        var searchJson = JsonSerializer.Serialize(new[] { new { entityType, entityId } }, JsonOpts);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {TableName}
            WHERE tenant_id = @tenantId
              AND (expires_at_utc IS NULL OR expires_at_utc > @now)
              AND linked_entities @> @searchJson::jsonb
            ORDER BY created_at_utc DESC
        ", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        cmd.Parameters.AddWithValue("searchJson", searchJson);

        var memories = new List<EnterpriseMemoryRecord>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                memories.Add(MapRecord(reader));
        }

        var layerDistribution = memories
            .GroupBy(m => m.Layer.ToString())
            .ToDictionary(g => g.Key, g => g.Count()) as IReadOnlyDictionary<string, int>;

        return new EntityMemoryView(entityType, entityId, memories, layerDistribution);
    }

    // ── GetTimelineAsync ────────────────────────────────────────────

    public async Task<IReadOnlyList<EnterpriseMemoryRecord>> GetTimelineAsync(
        Guid tenantId, MemoryLayer? layer = null, int limit = 100,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $@"
            SELECT * FROM {TableName}
            WHERE tenant_id = @tenantId
              AND (expires_at_utc IS NULL OR expires_at_utc > @now)";

        if (layer.HasValue) sql += " AND layer = @layer";
        sql += " ORDER BY created_at_utc DESC LIMIT @limit";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        if (layer.HasValue) cmd.Parameters.AddWithValue("layer", (int)layer.Value);
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<EnterpriseMemoryRecord>();
        while (await reader.ReadAsync(ct))
            results.Add(MapRecord(reader));
        return results;
    }

    // ── DeleteAsync ─────────────────────────────────────────────────

    public async Task<bool> DeleteAsync(
        Guid recordId, Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"DELETE FROM {TableName} WHERE id = @id AND tenant_id = @tenantId", conn);
        cmd.Parameters.AddWithValue("id", recordId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    // ── ExpireSessionMemoryAsync ────────────────────────────────────

    public async Task<int> ExpireSessionMemoryAsync(
        Guid tenantId, TimeSpan maxAge, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var threshold = DateTimeOffset.UtcNow - maxAge;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            DELETE FROM {TableName}
            WHERE tenant_id = @tenantId
              AND layer = @sessionLayer
              AND created_at_utc < @threshold
        ", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("sessionLayer", (int)MemoryLayer.Session);
        cmd.Parameters.AddWithValue("threshold", threshold);

        var deleted = await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Expired {Count} session memory records for tenant {TenantId}.", deleted, tenantId);
        return deleted;
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static void AddFilterParameters(NpgsqlCommand cmd, Guid tenantId, MemoryLayer? layer, string? category, string? tag)
    {
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        if (layer.HasValue) cmd.Parameters.AddWithValue("layer", (int)layer.Value);
        if (category is not null) cmd.Parameters.AddWithValue("category", category);
        if (tag is not null) cmd.Parameters.AddWithValue("tag", JsonSerializer.Serialize(new[] { tag }));
    }

    private static void AddParameters(NpgsqlCommand cmd, EnterpriseMemoryRecord r)
    {
        cmd.Parameters.AddWithValue("id", r.Id);
        cmd.Parameters.AddWithValue("tenantId", r.TenantId);
        cmd.Parameters.AddWithValue("layer", (int)r.Layer);
        cmd.Parameters.AddWithValue("category", r.Category);
        cmd.Parameters.AddWithValue("subject", r.Subject);
        cmd.Parameters.AddWithValue("content", r.Content);
        cmd.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(r.Metadata, JsonOpts));
        cmd.Parameters.AddWithValue("linkedEntities", JsonSerializer.Serialize(r.LinkedEntities, JsonOpts));
        cmd.Parameters.AddWithValue("tags", JsonSerializer.Serialize(r.Tags, JsonOpts));
        cmd.Parameters.AddWithValue("importance", r.Importance);
        cmd.Parameters.AddWithValue("createdBy", r.CreatedBy);
        cmd.Parameters.AddWithValue("createdAtUtc", r.CreatedAtUtc);
        cmd.Parameters.AddWithValue("expiresAtUtc", (object?)r.ExpiresAtUtc ?? DBNull.Value);
    }

    private static EnterpriseMemoryRecord MapRecord(NpgsqlDataReader r)
    {
        return new EnterpriseMemoryRecord(
            Id: r.GetGuid(r.GetOrdinal("id")),
            TenantId: r.GetGuid(r.GetOrdinal("tenant_id")),
            Layer: (MemoryLayer)r.GetInt32(r.GetOrdinal("layer")),
            Category: r.GetString(r.GetOrdinal("category")),
            Subject: r.GetString(r.GetOrdinal("subject")),
            Content: r.GetString(r.GetOrdinal("content")),
            Metadata: JsonSerializer.Deserialize<Dictionary<string, string>>(r.GetString(r.GetOrdinal("metadata")), JsonOpts) ?? new(),
            LinkedEntities: JsonSerializer.Deserialize<List<MemoryEntityLink>>(r.GetString(r.GetOrdinal("linked_entities")), JsonOpts) ?? new(),
            Tags: JsonSerializer.Deserialize<List<string>>(r.GetString(r.GetOrdinal("tags")), JsonOpts) ?? new(),
            Importance: r.GetDouble(r.GetOrdinal("importance")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAtUtc: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at_utc")),
            ExpiresAtUtc: r.IsDBNull(r.GetOrdinal("expires_at_utc")) ? null : r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("expires_at_utc")));
    }
}

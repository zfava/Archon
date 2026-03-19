using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.OperationalTwin;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresOperationalTwinStore : IOperationalTwinService
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresOperationalTwinStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private string EntitiesTable => $"{_schema}.twin_entities";
    private string DependenciesTable => $"{_schema}.twin_dependencies";
    private string KpisTable => $"{_schema}.twin_kpis";
    private string BottlenecksTable => $"{_schema}.twin_bottlenecks";
    private string ArtifactLinksTable => $"{_schema}.twin_artifact_links";

    public PostgresOperationalTwinStore(
        IOptions<PersistenceOptions> options,
        ILogger<PostgresOperationalTwinStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened
            ?? throw new ArgumentNullException(nameof(options), "ConnectionString is required.");
        _schema = options.Value.Schema;
        _logger = logger;
    }

    // ── Initialization ──────────────────────────────────────────────

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                CREATE SCHEMA IF NOT EXISTS {_schema};

                CREATE TABLE IF NOT EXISTS {EntitiesTable} (
                    id              uuid PRIMARY KEY,
                    tenant_id       uuid NOT NULL,
                    entity_type     int NOT NULL,
                    name            text NOT NULL,
                    description     text,
                    status          int NOT NULL,
                    properties      jsonb NOT NULL DEFAULT '{{}}'::jsonb,
                    tags            jsonb NOT NULL DEFAULT '[]',
                    created_by      text NOT NULL,
                    created_at_utc  timestamptz NOT NULL,
                    updated_at_utc  timestamptz NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_twin_entities_tenant_id   ON {EntitiesTable} (tenant_id);
                CREATE INDEX IF NOT EXISTS idx_twin_entities_entity_type ON {EntitiesTable} (entity_type);

                CREATE TABLE IF NOT EXISTS {DependenciesTable} (
                    id                  uuid PRIMARY KEY,
                    tenant_id           uuid NOT NULL,
                    from_entity_id      uuid NOT NULL,
                    to_entity_id        uuid NOT NULL,
                    type                int NOT NULL,
                    label               text,
                    criticality_score   double precision,
                    created_at_utc      timestamptz NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_twin_deps_tenant_id      ON {DependenciesTable} (tenant_id);
                CREATE INDEX IF NOT EXISTS idx_twin_deps_from_entity_id ON {DependenciesTable} (from_entity_id);

                CREATE TABLE IF NOT EXISTS {KpisTable} (
                    entity_id           uuid NOT NULL,
                    metric_name         text NOT NULL,
                    current_value       double precision NOT NULL,
                    target_value        double precision,
                    threshold_warning   double precision,
                    threshold_critical  double precision,
                    direction           int NOT NULL,
                    unit                text NOT NULL,
                    measured_at_utc     timestamptz NOT NULL,
                    PRIMARY KEY (entity_id, metric_name)
                );

                CREATE INDEX IF NOT EXISTS idx_twin_kpis_entity_id ON {KpisTable} (entity_id);

                CREATE TABLE IF NOT EXISTS {BottlenecksTable} (
                    id                  uuid PRIMARY KEY,
                    tenant_id           uuid NOT NULL,
                    affected_entity_id  uuid NOT NULL,
                    description         text NOT NULL,
                    severity            int NOT NULL,
                    root_cause          text,
                    is_resolved         bool NOT NULL DEFAULT false,
                    detected_at_utc     timestamptz NOT NULL,
                    resolved_at_utc     timestamptz
                );

                CREATE INDEX IF NOT EXISTS idx_twin_bottlenecks_tenant_id ON {BottlenecksTable} (tenant_id);

                CREATE TABLE IF NOT EXISTS {ArtifactLinksTable} (
                    id              uuid PRIMARY KEY,
                    twin_entity_id  uuid NOT NULL,
                    artifact_type   text NOT NULL,
                    artifact_id     text NOT NULL,
                    relationship    text NOT NULL,
                    linked_at_utc   timestamptz NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_twin_artifact_links_tenant ON {ArtifactLinksTable} (twin_entity_id);
            ";
            await cmd.ExecuteNonQueryAsync(ct);

            _initialized = true;
            _logger.LogInformation("PostgresOperationalTwinStore initialized (schema={Schema}).", _schema);
        }
        finally
        {
            _initLock.Release();
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Entities
    // ══════════════════════════════════════════════════════════════

    public async Task<TwinEntity> UpsertEntityAsync(TwinEntity entity, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {EntitiesTable}
                (id, tenant_id, entity_type, name, description, status, properties, tags,
                 created_by, created_at_utc, updated_at_utc)
            VALUES
                (@id, @tenantId, @entityType, @name, @description, @status, @properties::jsonb,
                 @tags::jsonb, @createdBy, @createdAtUtc, @updatedAtUtc)
            ON CONFLICT (id) DO UPDATE SET
                name = EXCLUDED.name,
                description = EXCLUDED.description,
                status = EXCLUDED.status,
                properties = EXCLUDED.properties,
                tags = EXCLUDED.tags,
                updated_at_utc = EXCLUDED.updated_at_utc
        ", conn);

        cmd.Parameters.AddWithValue("id", entity.Id);
        cmd.Parameters.AddWithValue("tenantId", entity.TenantId);
        cmd.Parameters.AddWithValue("entityType", (int)entity.EntityType);
        cmd.Parameters.AddWithValue("name", entity.Name);
        cmd.Parameters.AddWithValue("description", (object?)entity.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", (int)entity.Status);
        cmd.Parameters.AddWithValue("properties", JsonSerializer.Serialize(entity.Properties, JsonOpts));
        cmd.Parameters.AddWithValue("tags", JsonSerializer.Serialize(entity.Tags, JsonOpts));
        cmd.Parameters.AddWithValue("createdBy", entity.CreatedBy);
        cmd.Parameters.AddWithValue("createdAtUtc", entity.CreatedAtUtc);
        cmd.Parameters.AddWithValue("updatedAtUtc", entity.UpdatedAtUtc);

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Twin entity {EntityId} upserted.", entity.Id);
        return entity;
    }

    public async Task<TwinEntity?> GetEntityAsync(Guid entityId, Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {EntitiesTable} WHERE id = @id AND tenant_id = @tenantId", conn);
        cmd.Parameters.AddWithValue("id", entityId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapEntity(reader) : null;
    }

    public async Task<IReadOnlyList<TwinEntity>> ListEntitiesAsync(
        Guid tenantId, TwinEntityType? type = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"SELECT * FROM {EntitiesTable} WHERE tenant_id = @tenantId";
        if (type.HasValue) sql += " AND entity_type = @entityType";
        sql += " ORDER BY name";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        if (type.HasValue) cmd.Parameters.AddWithValue("entityType", (int)type.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<TwinEntity>();
        while (await reader.ReadAsync(ct))
            results.Add(MapEntity(reader));
        return results;
    }

    public async Task<bool> DeleteEntityAsync(Guid entityId, Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"DELETE FROM {EntitiesTable} WHERE id = @id AND tenant_id = @tenantId", conn);
        cmd.Parameters.AddWithValue("id", entityId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    // ══════════════════════════════════════════════════════════════
    //  Dependencies
    // ══════════════════════════════════════════════════════════════

    public async Task<TwinDependency> AddDependencyAsync(TwinDependency dep, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {DependenciesTable}
                (id, tenant_id, from_entity_id, to_entity_id, type, label, criticality_score, created_at_utc)
            VALUES
                (@id, @tenantId, @fromEntityId, @toEntityId, @type, @label, @criticalityScore, @createdAtUtc)
        ", conn);

        cmd.Parameters.AddWithValue("id", dep.Id);
        cmd.Parameters.AddWithValue("tenantId", dep.TenantId);
        cmd.Parameters.AddWithValue("fromEntityId", dep.FromEntityId);
        cmd.Parameters.AddWithValue("toEntityId", dep.ToEntityId);
        cmd.Parameters.AddWithValue("type", (int)dep.Type);
        cmd.Parameters.AddWithValue("label", (object?)dep.Label ?? DBNull.Value);
        cmd.Parameters.AddWithValue("criticalityScore", (object?)dep.CriticalityScore ?? DBNull.Value);
        cmd.Parameters.AddWithValue("createdAtUtc", dep.CreatedAtUtc);

        await cmd.ExecuteNonQueryAsync(ct);
        return dep;
    }

    public async Task<IReadOnlyList<TwinDependency>> GetDependenciesAsync(
        Guid entityId, Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {DependenciesTable}
            WHERE tenant_id = @tenantId AND (from_entity_id = @entityId OR to_entity_id = @entityId)
            ORDER BY created_at_utc
        ", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("entityId", entityId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<TwinDependency>();
        while (await reader.ReadAsync(ct))
            results.Add(MapDependency(reader));
        return results;
    }

    // ══════════════════════════════════════════════════════════════
    //  KPIs
    // ══════════════════════════════════════════════════════════════

    public async Task<TwinKpi> RecordKpiAsync(TwinKpi kpi, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {KpisTable}
                (entity_id, metric_name, current_value, target_value, threshold_warning,
                 threshold_critical, direction, unit, measured_at_utc)
            VALUES
                (@entityId, @metricName, @currentValue, @targetValue, @thresholdWarning,
                 @thresholdCritical, @direction, @unit, @measuredAtUtc)
            ON CONFLICT (entity_id, metric_name) DO UPDATE SET
                current_value = EXCLUDED.current_value,
                target_value = EXCLUDED.target_value,
                threshold_warning = EXCLUDED.threshold_warning,
                threshold_critical = EXCLUDED.threshold_critical,
                direction = EXCLUDED.direction,
                unit = EXCLUDED.unit,
                measured_at_utc = EXCLUDED.measured_at_utc
        ", conn);

        cmd.Parameters.AddWithValue("entityId", kpi.EntityId);
        cmd.Parameters.AddWithValue("metricName", kpi.MetricName);
        cmd.Parameters.AddWithValue("currentValue", kpi.CurrentValue);
        cmd.Parameters.AddWithValue("targetValue", (object?)kpi.TargetValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("thresholdWarning", (object?)kpi.ThresholdWarning ?? DBNull.Value);
        cmd.Parameters.AddWithValue("thresholdCritical", (object?)kpi.ThresholdCritical ?? DBNull.Value);
        cmd.Parameters.AddWithValue("direction", (int)kpi.Direction);
        cmd.Parameters.AddWithValue("unit", kpi.Unit);
        cmd.Parameters.AddWithValue("measuredAtUtc", kpi.MeasuredAtUtc);

        await cmd.ExecuteNonQueryAsync(ct);
        return kpi;
    }

    public async Task<IReadOnlyList<TwinKpi>> GetKpisAsync(Guid entityId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {KpisTable} WHERE entity_id = @entityId ORDER BY metric_name", conn);
        cmd.Parameters.AddWithValue("entityId", entityId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<TwinKpi>();
        while (await reader.ReadAsync(ct))
            results.Add(MapKpi(reader));
        return results;
    }

    // ══════════════════════════════════════════════════════════════
    //  Bottlenecks
    // ══════════════════════════════════════════════════════════════

    public async Task<TwinBottleneck> ReportBottleneckAsync(TwinBottleneck bottleneck, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {BottlenecksTable}
                (id, tenant_id, affected_entity_id, description, severity, root_cause,
                 is_resolved, detected_at_utc, resolved_at_utc)
            VALUES
                (@id, @tenantId, @affectedEntityId, @description, @severity, @rootCause,
                 @isResolved, @detectedAtUtc, @resolvedAtUtc)
        ", conn);

        cmd.Parameters.AddWithValue("id", bottleneck.Id);
        cmd.Parameters.AddWithValue("tenantId", bottleneck.TenantId);
        cmd.Parameters.AddWithValue("affectedEntityId", bottleneck.AffectedEntityId);
        cmd.Parameters.AddWithValue("description", bottleneck.Description);
        cmd.Parameters.AddWithValue("severity", (int)bottleneck.Severity);
        cmd.Parameters.AddWithValue("rootCause", (object?)bottleneck.RootCause ?? DBNull.Value);
        cmd.Parameters.AddWithValue("isResolved", bottleneck.IsResolved);
        cmd.Parameters.AddWithValue("detectedAtUtc", bottleneck.DetectedAtUtc);
        cmd.Parameters.AddWithValue("resolvedAtUtc", (object?)bottleneck.ResolvedAtUtc ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
        return bottleneck;
    }

    public async Task<TwinBottleneck?> ResolveBottleneckAsync(
        Guid bottleneckId, Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var now = DateTimeOffset.UtcNow;

        await using var cmd = new NpgsqlCommand($@"
            UPDATE {BottlenecksTable}
            SET is_resolved = true, resolved_at_utc = @now
            WHERE id = @id AND tenant_id = @tenantId
            RETURNING *
        ", conn);
        cmd.Parameters.AddWithValue("now", now);
        cmd.Parameters.AddWithValue("id", bottleneckId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapBottleneck(reader) : null;
    }

    public async Task<IReadOnlyList<TwinBottleneck>> ListBottlenecksAsync(
        Guid tenantId, bool activeOnly = true, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"SELECT * FROM {BottlenecksTable} WHERE tenant_id = @tenantId";
        if (activeOnly) sql += " AND is_resolved = false";
        sql += " ORDER BY detected_at_utc DESC";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<TwinBottleneck>();
        while (await reader.ReadAsync(ct))
            results.Add(MapBottleneck(reader));
        return results;
    }

    // ══════════════════════════════════════════════════════════════
    //  Artifact Links
    // ══════════════════════════════════════════════════════════════

    public async Task<TwinArtifactLink> LinkArtifactAsync(TwinArtifactLink link, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {ArtifactLinksTable}
                (id, twin_entity_id, artifact_type, artifact_id, relationship, linked_at_utc)
            VALUES
                (@id, @twinEntityId, @artifactType, @artifactId, @relationship, @linkedAtUtc)
        ", conn);

        cmd.Parameters.AddWithValue("id", link.Id);
        cmd.Parameters.AddWithValue("twinEntityId", link.TwinEntityId);
        cmd.Parameters.AddWithValue("artifactType", link.ArtifactType);
        cmd.Parameters.AddWithValue("artifactId", link.ArtifactId);
        cmd.Parameters.AddWithValue("relationship", link.Relationship);
        cmd.Parameters.AddWithValue("linkedAtUtc", link.LinkedAtUtc);

        await cmd.ExecuteNonQueryAsync(ct);
        return link;
    }

    public async Task<IReadOnlyList<TwinArtifactLink>> GetArtifactLinksAsync(
        Guid entityId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {ArtifactLinksTable} WHERE twin_entity_id = @entityId ORDER BY linked_at_utc", conn);
        cmd.Parameters.AddWithValue("entityId", entityId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<TwinArtifactLink>();
        while (await reader.ReadAsync(ct))
            results.Add(MapArtifactLink(reader));
        return results;
    }

    // ══════════════════════════════════════════════════════════════
    //  Overview
    // ══════════════════════════════════════════════════════════════

    public async Task<TwinOverview> GetOverviewAsync(Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Entity counts by type
        var entityCounts = new Dictionary<string, int>();
        await using (var cmd = new NpgsqlCommand($@"
            SELECT entity_type, COUNT(*) AS cnt
            FROM {EntitiesTable}
            WHERE tenant_id = @tenantId
            GROUP BY entity_type
        ", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var typeValue = (TwinEntityType)reader.GetInt32(reader.GetOrdinal("entity_type"));
                entityCounts[typeValue.ToString()] = reader.GetInt32(reader.GetOrdinal("cnt"));
            }
        }

        // Active bottlenecks
        var activeBottlenecks = await ListBottlenecksAsync(tenantId, activeOnly: true, ct);

        // Warning KPIs: where current_value exceeds warning threshold
        var warningKpis = new List<TwinKpi>();
        await using (var cmd = new NpgsqlCommand($@"
            SELECT k.* FROM {KpisTable} k
            INNER JOIN {EntitiesTable} e ON k.entity_id = e.id
            WHERE e.tenant_id = @tenantId
              AND k.threshold_warning IS NOT NULL
              AND (
                  (k.direction = @higherIsBetter AND k.current_value <= k.threshold_warning)
                  OR
                  (k.direction = @lowerIsBetter AND k.current_value >= k.threshold_warning)
              )
        ", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("higherIsBetter", (int)KpiDirection.HigherIsBetter);
            cmd.Parameters.AddWithValue("lowerIsBetter", (int)KpiDirection.LowerIsBetter);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                warningKpis.Add(MapKpi(reader));
        }

        // Total dependencies
        int totalDeps = 0;
        await using (var cmd = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM {DependenciesTable} WHERE tenant_id = @tenantId", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            totalDeps = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }

        return new TwinOverview(
            TenantId: tenantId,
            EntityCounts: entityCounts,
            ActiveBottlenecks: activeBottlenecks,
            WarningKpis: warningKpis,
            TotalDependencies: totalDeps,
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    // ── Row Mappers ─────────────────────────────────────────────────

    private static TwinEntity MapEntity(NpgsqlDataReader r)
    {
        return new TwinEntity(
            Id: r.GetGuid(r.GetOrdinal("id")),
            TenantId: r.GetGuid(r.GetOrdinal("tenant_id")),
            EntityType: (TwinEntityType)r.GetInt32(r.GetOrdinal("entity_type")),
            Name: r.GetString(r.GetOrdinal("name")),
            Description: r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")),
            Status: (TwinEntityStatus)r.GetInt32(r.GetOrdinal("status")),
            Properties: JsonSerializer.Deserialize<Dictionary<string, string>>(r.GetString(r.GetOrdinal("properties")), JsonOpts) ?? new(),
            Tags: JsonSerializer.Deserialize<List<string>>(r.GetString(r.GetOrdinal("tags")), JsonOpts) ?? new(),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAtUtc: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at_utc")),
            UpdatedAtUtc: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("updated_at_utc")));
    }

    private static TwinDependency MapDependency(NpgsqlDataReader r)
    {
        return new TwinDependency(
            Id: r.GetGuid(r.GetOrdinal("id")),
            TenantId: r.GetGuid(r.GetOrdinal("tenant_id")),
            FromEntityId: r.GetGuid(r.GetOrdinal("from_entity_id")),
            ToEntityId: r.GetGuid(r.GetOrdinal("to_entity_id")),
            Type: (DependencyType)r.GetInt32(r.GetOrdinal("type")),
            Label: r.IsDBNull(r.GetOrdinal("label")) ? null : r.GetString(r.GetOrdinal("label")),
            CriticalityScore: r.IsDBNull(r.GetOrdinal("criticality_score")) ? null : r.GetDouble(r.GetOrdinal("criticality_score")),
            CreatedAtUtc: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at_utc")));
    }

    private static TwinKpi MapKpi(NpgsqlDataReader r)
    {
        return new TwinKpi(
            EntityId: r.GetGuid(r.GetOrdinal("entity_id")),
            MetricName: r.GetString(r.GetOrdinal("metric_name")),
            CurrentValue: r.GetDouble(r.GetOrdinal("current_value")),
            TargetValue: r.IsDBNull(r.GetOrdinal("target_value")) ? null : r.GetDouble(r.GetOrdinal("target_value")),
            ThresholdWarning: r.IsDBNull(r.GetOrdinal("threshold_warning")) ? null : r.GetDouble(r.GetOrdinal("threshold_warning")),
            ThresholdCritical: r.IsDBNull(r.GetOrdinal("threshold_critical")) ? null : r.GetDouble(r.GetOrdinal("threshold_critical")),
            Direction: (KpiDirection)r.GetInt32(r.GetOrdinal("direction")),
            Unit: r.GetString(r.GetOrdinal("unit")),
            MeasuredAtUtc: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("measured_at_utc")));
    }

    private static TwinBottleneck MapBottleneck(NpgsqlDataReader r)
    {
        return new TwinBottleneck(
            Id: r.GetGuid(r.GetOrdinal("id")),
            TenantId: r.GetGuid(r.GetOrdinal("tenant_id")),
            AffectedEntityId: r.GetGuid(r.GetOrdinal("affected_entity_id")),
            Description: r.GetString(r.GetOrdinal("description")),
            Severity: (BottleneckSeverity)r.GetInt32(r.GetOrdinal("severity")),
            RootCause: r.IsDBNull(r.GetOrdinal("root_cause")) ? null : r.GetString(r.GetOrdinal("root_cause")),
            IsResolved: r.GetBoolean(r.GetOrdinal("is_resolved")),
            DetectedAtUtc: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("detected_at_utc")),
            ResolvedAtUtc: r.IsDBNull(r.GetOrdinal("resolved_at_utc")) ? null : r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("resolved_at_utc")));
    }

    private static TwinArtifactLink MapArtifactLink(NpgsqlDataReader r)
    {
        return new TwinArtifactLink(
            Id: r.GetGuid(r.GetOrdinal("id")),
            TwinEntityId: r.GetGuid(r.GetOrdinal("twin_entity_id")),
            ArtifactType: r.GetString(r.GetOrdinal("artifact_type")),
            ArtifactId: r.GetString(r.GetOrdinal("artifact_id")),
            Relationship: r.GetString(r.GetOrdinal("relationship")),
            LinkedAtUtc: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("linked_at_utc")));
    }
}

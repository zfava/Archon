using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Scenario;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresScenarioStore : IScenarioService
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresScenarioStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private string TableName => $"{_schema}.scenarios";

    public PostgresScenarioStore(
        IOptions<PersistenceOptions> options,
        ILogger<PostgresScenarioStore> logger)
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
        // See Scripts/008_create_scenarios.sql
        _initialized = true;
        return Task.CompletedTask;
    }

    // ── CreateScenarioAsync ─────────────────────────────────────────

    public async Task<Scenario> CreateScenarioAsync(Scenario scenario, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {TableName}
                (id, tenant_id, title, description, type, status, assumptions, projected_effects,
                 linked_kpis, linked_decisions, linked_entities, created_by, created_at_utc, updated_at_utc)
            VALUES
                (@id, @tenantId, @title, @description, @type, @status, @assumptions::jsonb,
                 @projectedEffects::jsonb, @linkedKpis::jsonb, @linkedDecisions::jsonb,
                 @linkedEntities::jsonb, @createdBy, @createdAtUtc, @updatedAtUtc)
        ", conn);

        cmd.Parameters.AddWithValue("id", scenario.Id);
        cmd.Parameters.AddWithValue("tenantId", scenario.TenantId);
        cmd.Parameters.AddWithValue("title", scenario.Title);
        cmd.Parameters.AddWithValue("description", (object?)scenario.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("type", (int)scenario.Type);
        cmd.Parameters.AddWithValue("status", (int)scenario.Status);
        cmd.Parameters.AddWithValue("assumptions", JsonSerializer.Serialize(scenario.Assumptions, JsonOpts));
        cmd.Parameters.AddWithValue("projectedEffects", JsonSerializer.Serialize(scenario.ProjectedEffects, JsonOpts));
        cmd.Parameters.AddWithValue("linkedKpis", JsonSerializer.Serialize(scenario.LinkedKpis, JsonOpts));
        cmd.Parameters.AddWithValue("linkedDecisions", JsonSerializer.Serialize(scenario.LinkedDecisions, JsonOpts));
        cmd.Parameters.AddWithValue("linkedEntities", JsonSerializer.Serialize(scenario.LinkedEntities, JsonOpts));
        cmd.Parameters.AddWithValue("createdBy", scenario.CreatedBy);
        cmd.Parameters.AddWithValue("createdAtUtc", scenario.CreatedAtUtc);
        cmd.Parameters.AddWithValue("updatedAtUtc", scenario.UpdatedAtUtc);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Scenario {ScenarioId} created.", scenario.Id);
        return scenario;
    }

    // ── UpdateAssumptionsAsync ──────────────────────────────────────

    public async Task<Scenario?> UpdateAssumptionsAsync(
        Guid scenarioId, Guid tenantId,
        IReadOnlyList<ScenarioAssumption> assumptions,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            UPDATE {TableName}
            SET assumptions = @assumptions::jsonb,
                updated_at_utc = @now
            WHERE id = @id AND tenant_id = @tenantId
        ", conn);

        cmd.Parameters.AddWithValue("assumptions", JsonSerializer.Serialize(assumptions, JsonOpts));
        cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        cmd.Parameters.AddWithValue("id", scenarioId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        if (rows == 0) return null;

        return await GetScenarioAsync(scenarioId, tenantId, ct);
    }

    // ── GetScenarioAsync ────────────────────────────────────────────

    public async Task<Scenario?> GetScenarioAsync(Guid scenarioId, Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {TableName} WHERE id = @id AND tenant_id = @tenantId", conn);
        cmd.Parameters.AddWithValue("id", scenarioId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapScenario(reader) : null;
    }

    // ── ListScenariosAsync ──────────────────────────────────────────

    public async Task<IReadOnlyList<Scenario>> ListScenariosAsync(
        Guid tenantId, ScenarioType? type = null,
        ScenarioStatus? status = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"SELECT * FROM {TableName} WHERE tenant_id = @tenantId";
        if (type.HasValue) sql += " AND type = @type";
        if (status.HasValue) sql += " AND status = @status";
        sql += " ORDER BY created_at_utc DESC";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        if (type.HasValue) cmd.Parameters.AddWithValue("type", (int)type.Value);
        if (status.HasValue) cmd.Parameters.AddWithValue("status", (int)status.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<Scenario>();
        while (await reader.ReadAsync(ct))
            results.Add(MapScenario(reader));
        return results;
    }

    // ── CompareScenariosAsync ───────────────────────────────────────

    public async Task<ScenarioComparison> CompareScenariosAsync(
        IReadOnlyList<Guid> scenarioIds, Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        // Load all requested scenarios
        var scenarios = new List<Scenario>();
        foreach (var id in scenarioIds)
        {
            var s = await GetScenarioAsync(id, tenantId, ct);
            if (s is not null) scenarios.Add(s);
        }

        // Build comparison axes from projected effects, grouped by metric
        var metricGroups = scenarios
            .SelectMany(s => s.ProjectedEffects.Select(e => new { ScenarioId = s.Id, Effect = e }))
            .GroupBy(x => x.Effect.Metric);

        var axes = new List<ScenarioComparisonAxis>();
        foreach (var group in metricGroups)
        {
            var valuesByScenario = new Dictionary<Guid, double?>();
            string? unit = null;

            foreach (var item in group)
            {
                valuesByScenario[item.ScenarioId] = item.Effect.ProjectedValue;
                unit ??= item.Effect.Unit;
            }

            // Ensure all scenario IDs are represented
            foreach (var s in scenarios)
            {
                if (!valuesByScenario.ContainsKey(s.Id))
                    valuesByScenario[s.Id] = null;
            }

            axes.Add(new ScenarioComparisonAxis(group.Key, unit, valuesByScenario));
        }

        return new ScenarioComparison(
            ScenarioIds: scenarios.Select(s => s.Id).ToList(),
            Axes: axes,
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    // ── DeleteScenarioAsync ─────────────────────────────────────────

    public async Task<bool> DeleteScenarioAsync(Guid scenarioId, Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"DELETE FROM {TableName} WHERE id = @id AND tenant_id = @tenantId", conn);
        cmd.Parameters.AddWithValue("id", scenarioId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    // ── Row Mapper ──────────────────────────────────────────────────

    private static Scenario MapScenario(NpgsqlDataReader r)
    {
        return new Scenario(
            Id: r.GetGuid(r.GetOrdinal("id")),
            TenantId: r.GetGuid(r.GetOrdinal("tenant_id")),
            Title: r.GetString(r.GetOrdinal("title")),
            Description: r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")),
            Type: (ScenarioType)r.GetInt32(r.GetOrdinal("type")),
            Status: (ScenarioStatus)r.GetInt32(r.GetOrdinal("status")),
            Assumptions: JsonSerializer.Deserialize<List<ScenarioAssumption>>(r.GetString(r.GetOrdinal("assumptions")), JsonOpts) ?? new(),
            ProjectedEffects: JsonSerializer.Deserialize<List<ProjectedEffect>>(r.GetString(r.GetOrdinal("projected_effects")), JsonOpts) ?? new(),
            LinkedKpis: JsonSerializer.Deserialize<List<ScenarioLink>>(r.GetString(r.GetOrdinal("linked_kpis")), JsonOpts) ?? new(),
            LinkedDecisions: JsonSerializer.Deserialize<List<ScenarioLink>>(r.GetString(r.GetOrdinal("linked_decisions")), JsonOpts) ?? new(),
            LinkedEntities: JsonSerializer.Deserialize<List<ScenarioLink>>(r.GetString(r.GetOrdinal("linked_entities")), JsonOpts) ?? new(),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAtUtc: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at_utc")),
            UpdatedAtUtc: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("updated_at_utc")));
    }
}

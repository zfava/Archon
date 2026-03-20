using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.AgentRegistry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresAgentRegistryStore : IAgentRegistryRepository
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresAgentRegistryStore> _logger;
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public PostgresAgentRegistryStore(
        IOptions<PersistenceOptions> options,
        ILogger<PostgresAgentRegistryStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened
            ?? throw new ArgumentNullException(nameof(options), "ConnectionString is required.");
        _schema = options.Value.Schema;
        _logger = logger;
    }

    private string AgentsTable => $"{_schema}.registered_agents";
    private string MetricsTable => $"{_schema}.agent_metrics";

    private Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return Task.CompletedTask;
        // Table creation is managed by DbUp migrations in ArchonAI.Migrations.
        // See Scripts/021_create_agent_registry.sql
        _initialized = true;
        return Task.CompletedTask;
    }

    // ── UpsertAgentAsync ────────────────────────────────────────

    public async Task<RegisteredAgent> UpsertAgentAsync(
        RegisteredAgent agent, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {AgentsTable}
                (id, name, description, version, status, capabilities, configuration,
                 registered_at_utc, last_heartbeat_utc, disabled_at_utc)
            VALUES
                (@id, @name, @description, @version, @status, @capabilities::jsonb, @configuration::jsonb,
                 @registeredAtUtc, @lastHeartbeatUtc, @disabledAtUtc)
            ON CONFLICT (id) DO UPDATE SET
                name = EXCLUDED.name,
                description = EXCLUDED.description,
                version = EXCLUDED.version,
                status = EXCLUDED.status,
                capabilities = EXCLUDED.capabilities,
                configuration = EXCLUDED.configuration,
                last_heartbeat_utc = EXCLUDED.last_heartbeat_utc,
                disabled_at_utc = EXCLUDED.disabled_at_utc
        ", conn);

        cmd.Parameters.AddWithValue("id", agent.Id);
        cmd.Parameters.AddWithValue("name", agent.Name);
        cmd.Parameters.AddWithValue("description", agent.Description);
        cmd.Parameters.AddWithValue("version", agent.Version);
        cmd.Parameters.AddWithValue("status", (int)agent.Status);
        cmd.Parameters.AddWithValue("capabilities", JsonSerializer.Serialize(agent.Capabilities, JsonOpts));
        cmd.Parameters.AddWithValue("configuration", JsonSerializer.Serialize(agent.Configuration, JsonOpts));
        cmd.Parameters.AddWithValue("registeredAtUtc", agent.RegisteredAtUtc);
        cmd.Parameters.AddWithValue("lastHeartbeatUtc", (object?)agent.LastHeartbeatUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("disabledAtUtc", (object?)agent.DisabledAtUtc ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Agent {AgentId} ({Name}) upserted.", agent.Id, agent.Name);
        return agent;
    }

    // ── GetAgentAsync ───────────────────────────────────────────

    public async Task<RegisteredAgent?> GetAgentAsync(
        Guid agentId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {AgentsTable} WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", agentId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapAgent(reader) : null;
    }

    // ── ListAgentsAsync ─────────────────────────────────────────

    public async Task<IReadOnlyList<RegisteredAgent>> ListAgentsAsync(
        RegisteredAgentStatus? status, string? capability,
        int offset, int limit, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"SELECT * FROM {AgentsTable} WHERE 1=1";
        var parameters = new List<NpgsqlParameter>();

        if (status.HasValue)
        {
            sql += " AND status = @status";
            parameters.Add(new NpgsqlParameter("status", (int)status.Value));
        }

        if (!string.IsNullOrEmpty(capability))
        {
            sql += " AND capabilities @> @capabilityFilter::jsonb";
            var filter = JsonSerializer.Serialize(
                new[] { new { name = capability } }, JsonOpts);
            parameters.Add(new NpgsqlParameter("capabilityFilter", filter));
        }

        sql += " ORDER BY name OFFSET @offset LIMIT @limit";
        parameters.Add(new NpgsqlParameter("offset", offset));
        parameters.Add(new NpgsqlParameter("limit", limit));

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters.ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<RegisteredAgent>();
        while (await reader.ReadAsync(ct))
            results.Add(MapAgent(reader));
        return results;
    }

    // ── RemoveAgentAsync ────────────────────────────────────────

    public async Task<bool> RemoveAgentAsync(
        Guid agentId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"DELETE FROM {AgentsTable} WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", agentId);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        if (rows > 0)
            _logger.LogInformation("Agent {AgentId} removed.", agentId);
        return rows > 0;
    }

    // ── CountAgentsAsync ────────────────────────────────────────

    public async Task<int> CountAgentsAsync(
        RegisteredAgentStatus? status = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = $"SELECT COUNT(*) FROM {AgentsTable}";
        if (status.HasValue)
            sql += " WHERE status = @status";

        await using var cmd = new NpgsqlCommand(sql, conn);
        if (status.HasValue)
            cmd.Parameters.AddWithValue("status", (int)status.Value);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    // ── AddMetricAsync ──────────────────────────────────────────

    public async Task AddMetricAsync(
        AgentMetricSnapshot metric, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {MetricsTable}
                (id, agent_id, total_executions, successful_executions, failed_executions,
                 average_latency_ms, p95_latency_ms, uptime_percent, collected_at_utc)
            VALUES
                (@id, @agentId, @totalExecutions, @successfulExecutions, @failedExecutions,
                 @averageLatencyMs, @p95LatencyMs, @uptimePercent, @collectedAtUtc)
        ", conn);

        cmd.Parameters.AddWithValue("id", metric.Id);
        cmd.Parameters.AddWithValue("agentId", metric.AgentId);
        cmd.Parameters.AddWithValue("totalExecutions", metric.TotalExecutions);
        cmd.Parameters.AddWithValue("successfulExecutions", metric.SuccessfulExecutions);
        cmd.Parameters.AddWithValue("failedExecutions", metric.FailedExecutions);
        cmd.Parameters.AddWithValue("averageLatencyMs", metric.AverageLatencyMs);
        cmd.Parameters.AddWithValue("p95LatencyMs", metric.P95LatencyMs);
        cmd.Parameters.AddWithValue("uptimePercent", metric.UptimePercent);
        cmd.Parameters.AddWithValue("collectedAtUtc", metric.CollectedAtUtc);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── GetMetricsAsync ─────────────────────────────────────────

    public async Task<IReadOnlyList<AgentMetricSnapshot>> GetMetricsAsync(
        Guid agentId, int limit, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {MetricsTable}
            WHERE agent_id = @agentId
            ORDER BY collected_at_utc DESC
            LIMIT @limit
        ", conn);
        cmd.Parameters.AddWithValue("agentId", agentId);
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<AgentMetricSnapshot>();
        while (await reader.ReadAsync(ct))
            results.Add(MapMetric(reader));
        return results;
    }

    // ── GetRecentMetricsAsync ───────────────────────────────────

    public async Task<IReadOnlyList<AgentMetricSnapshot>> GetRecentMetricsAsync(
        int limit = 50, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {MetricsTable}
            ORDER BY collected_at_utc DESC
            LIMIT @limit
        ", conn);
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<AgentMetricSnapshot>();
        while (await reader.ReadAsync(ct))
            results.Add(MapMetric(reader));
        return results;
    }

    // ── Row Mappers ─────────────────────────────────────────────

    private static RegisteredAgent MapAgent(NpgsqlDataReader reader)
    {
        var capabilities = JsonSerializer.Deserialize<List<AgentCapabilityRecord>>(
            reader.GetString(reader.GetOrdinal("capabilities")), JsonOpts) ?? new();

        var configuration = JsonSerializer.Deserialize<Dictionary<string, string>>(
            reader.GetString(reader.GetOrdinal("configuration")), JsonOpts) ?? new();

        var heartbeatOrd = reader.GetOrdinal("last_heartbeat_utc");
        var disabledOrd = reader.GetOrdinal("disabled_at_utc");

        return new RegisteredAgent(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("name")),
            reader.GetString(reader.GetOrdinal("description")),
            reader.GetString(reader.GetOrdinal("version")),
            (RegisteredAgentStatus)reader.GetInt32(reader.GetOrdinal("status")),
            capabilities,
            configuration,
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("registered_at_utc")),
            reader.IsDBNull(heartbeatOrd) ? null : reader.GetFieldValue<DateTimeOffset>(heartbeatOrd),
            reader.IsDBNull(disabledOrd) ? null : reader.GetFieldValue<DateTimeOffset>(disabledOrd));
    }

    private static AgentMetricSnapshot MapMetric(NpgsqlDataReader reader)
    {
        return new AgentMetricSnapshot(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetGuid(reader.GetOrdinal("agent_id")),
            reader.GetInt64(reader.GetOrdinal("total_executions")),
            reader.GetInt64(reader.GetOrdinal("successful_executions")),
            reader.GetInt64(reader.GetOrdinal("failed_executions")),
            reader.GetDouble(reader.GetOrdinal("average_latency_ms")),
            reader.GetDouble(reader.GetOrdinal("p95_latency_ms")),
            reader.GetDouble(reader.GetOrdinal("uptime_percent")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("collected_at_utc")));
    }
}

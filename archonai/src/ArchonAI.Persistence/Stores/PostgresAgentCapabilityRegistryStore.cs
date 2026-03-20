using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Persistence.Stores;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IAgentCapabilityRegistry"/>.
/// Replaces <see cref="InMemoryAgentCapabilityRegistry"/> for multi-instance deployments,
/// ensuring all instances share the same agent performance data and make consistent
/// agent selection decisions.
/// </summary>
public sealed class PostgresAgentCapabilityRegistryStore : IAgentCapabilityRegistry
{
    private const double SuccessWeight = 0.40;
    private const double LatencyWeight = 0.30;
    private const double CostWeight = 0.20;
    private const double ThroughputWeight = 0.10;
    private const int LatencySampleLimit = 1000;

    private readonly string _connectionString;
    private readonly string _schema;
    private readonly IEventBus _eventBus;
    private readonly ILogger<PostgresAgentCapabilityRegistryStore> _logger;
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public PostgresAgentCapabilityRegistryStore(
        IOptions<PersistenceOptions> options,
        IEventBus eventBus,
        ILogger<PostgresAgentCapabilityRegistryStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened
            ?? throw new ArgumentNullException(nameof(options), "ConnectionString is required.");
        _schema = options.Value.Schema;
        _eventBus = eventBus;
        _logger = logger;
    }

    private string ProfilesTable => $"{_schema}.agent_capability_profiles";
    private string SamplesTable => $"{_schema}.agent_execution_samples";

    private Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return Task.CompletedTask;
        _initialized = true;
        return Task.CompletedTask;
    }

    // ── RegisterOrUpdateAgentAsync ──────────────────────────────

    public async Task RegisterOrUpdateAgentAsync(
        Agent agent,
        IReadOnlyList<string> tools,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        bool isNew;

        await using (var checkCmd = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM {ProfilesTable} WHERE agent_id = @id", conn))
        {
            checkCmd.Parameters.AddWithValue("id", agent.Id);
            isNew = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(cancellationToken)) == 0;
        }

        var capabilities = agent.Capabilities.Select(c => c.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {ProfilesTable}
                (agent_id, agent_name, version, capabilities, tools, permissions, updated_at_utc)
            VALUES
                (@id, @name, @version, @capabilities::jsonb, @tools::jsonb, @permissions::jsonb, @updatedAt)
            ON CONFLICT (agent_id) DO UPDATE SET
                agent_name = EXCLUDED.agent_name,
                version = EXCLUDED.version,
                capabilities = EXCLUDED.capabilities,
                tools = EXCLUDED.tools,
                permissions = EXCLUDED.permissions,
                updated_at_utc = EXCLUDED.updated_at_utc
        ", conn);

        cmd.Parameters.AddWithValue("id", agent.Id);
        cmd.Parameters.AddWithValue("name", agent.Name);
        cmd.Parameters.AddWithValue("version", agent.Version);
        cmd.Parameters.AddWithValue("capabilities", JsonSerializer.Serialize(capabilities, JsonOpts));
        cmd.Parameters.AddWithValue("tools",
            JsonSerializer.Serialize(tools.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), JsonOpts));
        cmd.Parameters.AddWithValue("permissions",
            JsonSerializer.Serialize(permissions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), JsonOpts));
        cmd.Parameters.AddWithValue("updatedAt", DateTimeOffset.UtcNow);

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        string eventType = isNew ? "registry.agent.registered" : "registry.agent.updated";
        await _eventBus.PublishAsync(new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: eventType,
            Source: "agent-capability-registry",
            CorrelationId: agent.Id,
            Payload: new Dictionary<string, string>
            {
                ["agentId"] = agent.Id.ToString(),
                ["agentName"] = agent.Name,
                ["version"] = agent.Version,
                ["capabilities"] = string.Join(",", capabilities),
                ["tools"] = string.Join(",", tools)
            },
            OccurredAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        _logger.LogInformation("Registry {Action} agent {AgentName} ({AgentId})",
            isNew ? "registered" : "updated", agent.Name, agent.Id);
    }

    // ── RegisterSupportedTaskTypesAsync ─────────────────────────

    public async Task RegisterSupportedTaskTypesAsync(
        Guid agentId,
        IReadOnlyList<string> taskTypes,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // Merge new task types with existing ones
        await using var readCmd = new NpgsqlCommand(
            $"SELECT supported_task_types FROM {ProfilesTable} WHERE agent_id = @id", conn);
        readCmd.Parameters.AddWithValue("id", agentId);

        var existingJson = await readCmd.ExecuteScalarAsync(cancellationToken) as string;
        if (existingJson is null) return;

        var existing = JsonSerializer.Deserialize<List<string>>(existingJson, JsonOpts) ?? new();
        var merged = existing.Concat(taskTypes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        await using var updateCmd = new NpgsqlCommand($@"
            UPDATE {ProfilesTable}
            SET supported_task_types = @taskTypes::jsonb, updated_at_utc = @updatedAt
            WHERE agent_id = @id
        ", conn);
        updateCmd.Parameters.AddWithValue("id", agentId);
        updateCmd.Parameters.AddWithValue("taskTypes", JsonSerializer.Serialize(merged, JsonOpts));
        updateCmd.Parameters.AddWithValue("updatedAt", DateTimeOffset.UtcNow);

        await updateCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── ReportExecutionAsync ────────────────────────────────────

    public async Task ReportExecutionAsync(
        Guid agentId,
        string taskType,
        bool success,
        double latencyMs,
        decimal cost,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var safeLatency = Math.Max(0, latencyMs);
        var safeCost = Math.Max(0, cost);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // Insert execution sample
        await using var sampleCmd = new NpgsqlCommand($@"
            INSERT INTO {SamplesTable} (agent_id, task_type, success, latency_ms, cost, recorded_at_utc)
            VALUES (@agentId, @taskType, @success, @latency, @cost, @recordedAt)
        ", conn);
        sampleCmd.Parameters.AddWithValue("agentId", agentId);
        sampleCmd.Parameters.AddWithValue("taskType", (object?)taskType ?? DBNull.Value);
        sampleCmd.Parameters.AddWithValue("success", success);
        sampleCmd.Parameters.AddWithValue("latency", safeLatency);
        sampleCmd.Parameters.AddWithValue("cost", safeCost);
        sampleCmd.Parameters.AddWithValue("recordedAt", DateTimeOffset.UtcNow);
        await sampleCmd.ExecuteNonQueryAsync(cancellationToken);

        // Compute updated aggregates from recent samples
        await using var statsCmd = new NpgsqlCommand($@"
            WITH recent AS (
                SELECT latency_ms, success, cost
                FROM {SamplesTable}
                WHERE agent_id = @agentId
                ORDER BY recorded_at_utc DESC
                LIMIT {LatencySampleLimit}
            ),
            agg AS (
                SELECT
                    COUNT(*) AS total,
                    COUNT(*) FILTER (WHERE success) AS successes,
                    COUNT(*) FILTER (WHERE NOT success) AS failures,
                    AVG(latency_ms) AS avg_latency,
                    PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY latency_ms) AS p95_latency,
                    AVG(cost) AS avg_cost
                FROM recent
            )
            UPDATE {ProfilesTable}
            SET executions = agg.total,
                success_count = agg.successes,
                failure_count = agg.failures,
                average_latency_ms = COALESCE(agg.avg_latency, 0),
                p95_latency_ms = COALESCE(agg.p95_latency, 0),
                average_cost = COALESCE(agg.avg_cost, 0),
                success_rate = CASE WHEN agg.total > 0 THEN agg.successes::double precision / agg.total ELSE 0 END,
                supported_task_types = CASE
                    WHEN @taskType IS NOT NULL AND NOT {ProfilesTable}.supported_task_types @> to_jsonb(@taskType::text)
                    THEN {ProfilesTable}.supported_task_types || to_jsonb(@taskType::text)
                    ELSE {ProfilesTable}.supported_task_types
                END,
                updated_at_utc = @updatedAt
            FROM agg
            WHERE {ProfilesTable}.agent_id = @agentId
        ", conn);
        statsCmd.Parameters.AddWithValue("agentId", agentId);
        statsCmd.Parameters.AddWithValue("taskType", (object?)taskType ?? DBNull.Value);
        statsCmd.Parameters.AddWithValue("updatedAt", DateTimeOffset.UtcNow);
        await statsCmd.ExecuteNonQueryAsync(cancellationToken);

        // Prune old samples beyond the window
        await using var pruneCmd = new NpgsqlCommand($@"
            DELETE FROM {SamplesTable}
            WHERE agent_id = @agentId AND id NOT IN (
                SELECT id FROM {SamplesTable}
                WHERE agent_id = @agentId
                ORDER BY recorded_at_utc DESC
                LIMIT {LatencySampleLimit}
            )
        ", conn);
        pruneCmd.Parameters.AddWithValue("agentId", agentId);
        await pruneCmd.ExecuteNonQueryAsync(cancellationToken);

        await _eventBus.PublishAsync(new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: success ? "registry.execution.success" : "registry.execution.failure",
            Source: "agent-capability-registry",
            CorrelationId: agentId,
            Payload: new Dictionary<string, string>
            {
                ["agentId"] = agentId.ToString(),
                ["taskType"] = taskType ?? "",
                ["success"] = success.ToString(),
                ["latencyMs"] = safeLatency.ToString("F2"),
                ["cost"] = safeCost.ToString()
            },
            OccurredAtUtc: DateTimeOffset.UtcNow), cancellationToken);
    }

    // ── QueryByCapabilityAsync ──────────────────────────────────

    public async Task<IReadOnlyList<AgentCapabilityProfile>> QueryByCapabilityAsync(
        string capability, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {ProfilesTable}
            WHERE NOT is_suspended
              AND capabilities @> to_jsonb(@capability::text)
            ORDER BY success_rate DESC
        ", conn);
        cmd.Parameters.AddWithValue("capability", capability);

        return await ReadProfilesAsync(cmd, cancellationToken);
    }

    // ── QueryByTaskTypeAsync ────────────────────────────────────

    public async Task<IReadOnlyList<AgentCapabilityProfile>> QueryByTaskTypeAsync(
        string taskType, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {ProfilesTable}
            WHERE NOT is_suspended
              AND supported_task_types @> to_jsonb(@taskType::text)
            ORDER BY success_rate DESC
        ", conn);
        cmd.Parameters.AddWithValue("taskType", taskType);

        return await ReadProfilesAsync(cmd, cancellationToken);
    }

    // ── SelectBestAgentAsync ────────────────────────────────────

    public async Task<AgentSelectionResult?> SelectBestAgentAsync(
        string requiredCapability, string? taskType,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {ProfilesTable}
            WHERE NOT is_suspended
              AND capabilities @> to_jsonb(@capability::text)
        ", conn);
        cmd.Parameters.AddWithValue("capability", requiredCapability);

        var candidates = await ReadProfilesAsync(cmd, cancellationToken);
        if (candidates.Count == 0) return null;

        var taskTypeMatches = taskType is not null
            ? candidates.Where(p => p.SupportedTaskTypes.Any(t =>
                t.Equals(taskType, StringComparison.OrdinalIgnoreCase))).ToList()
            : null;

        var pool = taskTypeMatches is { Count: > 0 } ? taskTypeMatches : candidates;

        var scored = pool
            .Select(p => (Profile: p, Score: ComputeAgentScore(p)))
            .OrderByDescending(x => x.Score)
            .First();

        string reason = BuildSelectionReason(scored.Profile, taskType, taskTypeMatches?.Count ?? 0);

        var result = new AgentSelectionResult(
            AgentId: scored.Profile.AgentId,
            AgentName: scored.Profile.AgentName,
            Score: scored.Score,
            SuccessRate: scored.Profile.SuccessRate,
            AverageLatencyMs: scored.Profile.AverageLatencyMs,
            AverageCost: scored.Profile.AverageCost,
            SelectionReason: reason);

        _logger.LogInformation(
            "Selected agent {AgentName} (score={Score:F3}) for capability '{Capability}' taskType='{TaskType}'",
            result.AgentName, result.Score, requiredCapability, taskType ?? "any");

        return result;
    }

    // ── GetAgentAsync ───────────────────────────────────────────

    public async Task<AgentCapabilityProfile?> GetAgentAsync(
        Guid agentId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {ProfilesTable} WHERE agent_id = @id", conn);
        cmd.Parameters.AddWithValue("id", agentId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapProfile(reader) : null;
    }

    // ── GetAllAsync ─────────────────────────────────────────────

    public async Task<IReadOnlyList<AgentCapabilityProfile>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {ProfilesTable} ORDER BY agent_name", conn);

        return await ReadProfilesAsync(cmd, cancellationToken);
    }

    // ── GetPerformanceSnapshotAsync ─────────────────────────────

    public async Task<AgentPerformanceSnapshot?> GetPerformanceSnapshotAsync(
        Guid agentId, CancellationToken cancellationToken = default)
    {
        var profile = await GetAgentAsync(agentId, cancellationToken);
        if (profile is null) return null;

        return new AgentPerformanceSnapshot(
            AgentId: profile.AgentId,
            AgentName: profile.AgentName,
            AverageLatencyMs: profile.AverageLatencyMs,
            P95LatencyMs: profile.P95LatencyMs,
            AverageCost: profile.AverageCost,
            Executions: profile.Executions,
            SuccessRate: profile.SuccessRate,
            Throughput: profile.Throughput,
            Score: ComputeAgentScore(profile),
            SnapshotAtUtc: DateTimeOffset.UtcNow);
    }

    // ── SuspendAgentAsync ───────────────────────────────────────

    public async Task SuspendAgentAsync(
        Guid agentId, string reason,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand($@"
            UPDATE {ProfilesTable}
            SET is_suspended = true, suspend_reason = @reason, updated_at_utc = @updatedAt
            WHERE agent_id = @id
        ", conn);
        cmd.Parameters.AddWithValue("id", agentId);
        cmd.Parameters.AddWithValue("reason", reason);
        cmd.Parameters.AddWithValue("updatedAt", DateTimeOffset.UtcNow);

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (rows > 0)
            _logger.LogWarning("Agent {AgentId} suspended in capability registry: {Reason}", agentId, reason);
    }

    // ── ReinstateAgentAsync ─────────────────────────────────────

    public async Task ReinstateAgentAsync(
        Guid agentId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand($@"
            UPDATE {ProfilesTable}
            SET is_suspended = false, suspend_reason = NULL, updated_at_utc = @updatedAt
            WHERE agent_id = @id AND is_suspended = true
        ", conn);
        cmd.Parameters.AddWithValue("id", agentId);
        cmd.Parameters.AddWithValue("updatedAt", DateTimeOffset.UtcNow);

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (rows > 0)
            _logger.LogInformation("Agent {AgentId} reinstated in capability registry", agentId);
    }

    // ── Scoring (same logic as InMemoryAgentCapabilityRegistry) ─

    private static double ComputeAgentScore(AgentCapabilityProfile profile)
    {
        if (profile.Executions == 0) return 0.5;

        double successScore = profile.SuccessRate;
        double latencyScore = 1.0 / (1.0 + (profile.AverageLatencyMs / 1000.0));
        double costScore = 1.0 / (1.0 + (double)profile.AverageCost);
        double throughputScore = Math.Min(1.0, profile.Throughput / 10.0);

        return (SuccessWeight * successScore)
             + (LatencyWeight * latencyScore)
             + (CostWeight * costScore)
             + (ThroughputWeight * throughputScore);
    }

    private static string BuildSelectionReason(AgentCapabilityProfile profile, string? taskType, int taskTypeMatchCount)
    {
        var parts = new List<string>();

        if (taskType is not null && profile.SupportedTaskTypes.Any(t =>
            t.Equals(taskType, StringComparison.OrdinalIgnoreCase)))
            parts.Add($"supports task type '{taskType}'");

        if (profile.Executions > 0)
        {
            parts.Add($"success rate {profile.SuccessRate:P0}");
            parts.Add($"avg latency {profile.AverageLatencyMs:F0}ms");
            parts.Add($"avg cost {profile.AverageCost:F4}");
        }
        else
        {
            parts.Add("no execution history (neutral score)");
        }

        if (taskTypeMatchCount == 0 && taskType is not null)
            parts.Add($"no agents explicitly support task type '{taskType}', selected by capability match");

        return string.Join("; ", parts);
    }

    // ── Row Mapper ──────────────────────────────────────────────

    private static async Task<IReadOnlyList<AgentCapabilityProfile>> ReadProfilesAsync(
        NpgsqlCommand cmd, CancellationToken ct)
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<AgentCapabilityProfile>();
        while (await reader.ReadAsync(ct))
            results.Add(MapProfile(reader));
        return results;
    }

    private static AgentCapabilityProfile MapProfile(NpgsqlDataReader reader)
    {
        var suspendReasonOrd = reader.GetOrdinal("suspend_reason");

        return new AgentCapabilityProfile(
            AgentId: reader.GetGuid(reader.GetOrdinal("agent_id")),
            AgentName: reader.GetString(reader.GetOrdinal("agent_name")),
            Version: reader.GetString(reader.GetOrdinal("version")),
            Capabilities: JsonSerializer.Deserialize<string[]>(
                reader.GetString(reader.GetOrdinal("capabilities")), JsonOpts) ?? Array.Empty<string>(),
            Tools: JsonSerializer.Deserialize<string[]>(
                reader.GetString(reader.GetOrdinal("tools")), JsonOpts) ?? Array.Empty<string>(),
            Permissions: JsonSerializer.Deserialize<string[]>(
                reader.GetString(reader.GetOrdinal("permissions")), JsonOpts) ?? Array.Empty<string>(),
            SupportedTaskTypes: JsonSerializer.Deserialize<string[]>(
                reader.GetString(reader.GetOrdinal("supported_task_types")), JsonOpts) ?? Array.Empty<string>(),
            AverageLatencyMs: reader.GetDouble(reader.GetOrdinal("average_latency_ms")),
            P95LatencyMs: reader.GetDouble(reader.GetOrdinal("p95_latency_ms")),
            AverageCost: reader.GetDecimal(reader.GetOrdinal("average_cost")),
            Executions: reader.GetInt64(reader.GetOrdinal("executions")),
            SuccessCount: reader.GetInt64(reader.GetOrdinal("success_count")),
            FailureCount: reader.GetInt64(reader.GetOrdinal("failure_count")),
            SuccessRate: reader.GetDouble(reader.GetOrdinal("success_rate")),
            Throughput: reader.GetDouble(reader.GetOrdinal("throughput")),
            UpdatedAtUtc: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at_utc")),
            IsSuspended: reader.GetBoolean(reader.GetOrdinal("is_suspended")),
            SuspendReason: reader.IsDBNull(suspendReasonOrd) ? null : reader.GetString(suspendReasonOrd));
    }
}

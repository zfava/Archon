using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.ControlPlane;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Persistence.Stores;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IControlPlaneAlertStore"/>.
/// Provides shared, durable storage for system pause state, active alerts,
/// and recent agent activity events across multiple application instances.
/// </summary>
public sealed class PostgresControlPlaneAlertStore : IControlPlaneAlertStore
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresControlPlaneAlertStore> _logger;
    private bool _initialized;

    public PostgresControlPlaneAlertStore(
        IOptions<PersistenceOptions> options,
        ILogger<PostgresControlPlaneAlertStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened
            ?? throw new ArgumentNullException(nameof(options), "ConnectionString is required.");
        _schema = options.Value.Schema;
        _logger = logger;
    }

    private string SystemStateTable => $"{_schema}.control_plane_system_state";
    private string AlertsTable => $"{_schema}.control_plane_alerts";
    private string EventsTable => $"{_schema}.control_plane_agent_events";

    private Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return Task.CompletedTask;
        _initialized = true;
        return Task.CompletedTask;
    }

    // ── System pause state ──────────────────────────────────────

    public async Task SetPauseStateAsync(
        bool isPaused, string? reason, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            UPDATE {SystemStateTable}
            SET is_paused = @isPaused, pause_reason = @reason, updated_at_utc = @updatedAt
            WHERE id = 1
        ", conn);
        cmd.Parameters.AddWithValue("isPaused", isPaused);
        cmd.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("updatedAt", DateTimeOffset.UtcNow);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("System pause state set to {IsPaused} (reason: {Reason})", isPaused, reason);
    }

    public async Task<(bool IsPaused, string? Reason)> GetPauseStateAsync(
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT is_paused, pause_reason FROM {SystemStateTable} WHERE id = 1", conn);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var isPaused = reader.GetBoolean(0);
            var reason = reader.IsDBNull(1) ? null : reader.GetString(1);
            return (isPaused, reason);
        }

        return (false, null);
    }

    // ── Alert management ────────────────────────────────────────

    public async Task UpsertAlertAsync(
        SystemAlert alert, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {AlertsTable}
                (alert_id, severity, component, message, is_acknowledged, raised_at_utc)
            VALUES
                (@alertId, @severity, @component, @message, @isAcknowledged, @raisedAt)
            ON CONFLICT (alert_id) DO UPDATE SET
                is_acknowledged = EXCLUDED.is_acknowledged
        ", conn);

        cmd.Parameters.AddWithValue("alertId", alert.AlertId);
        cmd.Parameters.AddWithValue("severity", alert.Severity);
        cmd.Parameters.AddWithValue("component", alert.Component);
        cmd.Parameters.AddWithValue("message", alert.Message);
        cmd.Parameters.AddWithValue("isAcknowledged", alert.IsAcknowledged);
        cmd.Parameters.AddWithValue("raisedAt", alert.RaisedAtUtc);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<SystemAlert>> GetActiveAlertsAsync(
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {AlertsTable}
            WHERE NOT is_acknowledged
            ORDER BY raised_at_utc DESC
        ", conn);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<SystemAlert>();
        while (await reader.ReadAsync(ct))
            results.Add(MapAlert(reader));
        return results;
    }

    public async Task AcknowledgeAlertAsync(
        Guid alertId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            UPDATE {AlertsTable}
            SET is_acknowledged = true
            WHERE alert_id = @alertId
        ", conn);
        cmd.Parameters.AddWithValue("alertId", alertId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task EvictStaleAlertsAsync(
        int maxAlerts = 500, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Keep the @keep most-recent acknowledged alerts; delete the rest.
        // Business rule: only acknowledged alerts are eligible for eviction.
        // Unacknowledged (active) alerts are never pruned by this method.
        await using var cmd = new NpgsqlCommand($@"
            DELETE FROM {AlertsTable}
            WHERE alert_id IN (
                SELECT alert_id FROM {AlertsTable}
                WHERE is_acknowledged
                ORDER BY raised_at_utc DESC
                OFFSET @keep
            )
        ", conn);
        cmd.Parameters.AddWithValue("keep", maxAlerts);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Recent agent activity events ────────────────────────────

    public async Task AddAgentEventAsync(
        AgentActivityEvent evt, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {EventsTable}
                (agent_id, agent_name, event_type, description, occurred_at_utc)
            VALUES
                (@agentId, @agentName, @eventType, @description, @occurredAt)
        ", conn);

        cmd.Parameters.AddWithValue("agentId", evt.AgentId);
        cmd.Parameters.AddWithValue("agentName", evt.AgentName);
        cmd.Parameters.AddWithValue("eventType", evt.EventType);
        cmd.Parameters.AddWithValue("description", evt.Description);
        cmd.Parameters.AddWithValue("occurredAt", evt.OccurredAtUtc);

        await cmd.ExecuteNonQueryAsync(ct);

        // Prune old events beyond the cap
        await using var pruneCmd = new NpgsqlCommand($@"
            DELETE FROM {EventsTable}
            WHERE id NOT IN (
                SELECT id FROM {EventsTable}
                ORDER BY occurred_at_utc DESC, id DESC
                LIMIT 200
            )
        ", conn);
        await pruneCmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<AgentActivityEvent>> GetRecentEventsAsync(
        int limit = 50, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {EventsTable}
            ORDER BY occurred_at_utc DESC, id DESC
            LIMIT @limit
        ", conn);
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<AgentActivityEvent>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new AgentActivityEvent(
                AgentId: reader.GetGuid(reader.GetOrdinal("agent_id")),
                AgentName: reader.GetString(reader.GetOrdinal("agent_name")),
                EventType: reader.GetString(reader.GetOrdinal("event_type")),
                Description: reader.GetString(reader.GetOrdinal("description")),
                OccurredAtUtc: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("occurred_at_utc"))));
        }
        return results;
    }

    // ── Row Mapper ──────────────────────────────────────────────

    private static SystemAlert MapAlert(NpgsqlDataReader reader)
    {
        return new SystemAlert(
            AlertId: reader.GetGuid(reader.GetOrdinal("alert_id")),
            Severity: reader.GetString(reader.GetOrdinal("severity")),
            Component: reader.GetString(reader.GetOrdinal("component")),
            Message: reader.GetString(reader.GetOrdinal("message")),
            IsAcknowledged: reader.GetBoolean(reader.GetOrdinal("is_acknowledged")),
            RaisedAtUtc: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("raised_at_utc")));
    }
}

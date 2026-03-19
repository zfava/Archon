using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.ActionSafety;
using ArchonAI.Core.Models.ProofAnalytics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresActionSafetyStore : IActionSafetyService
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresActionSafetyStore> _logger;
    private readonly IEventBus _eventBus;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public PostgresActionSafetyStore(
        IOptions<PersistenceOptions> options,
        ILogger<PostgresActionSafetyStore> logger,
        IEventBus eventBus)
    {
        _connectionString = options.Value.ConnectionStringHardened
            ?? throw new ArgumentNullException(nameof(options), "ConnectionString is required.");
        _schema = options.Value.Schema;
        _logger = logger;
        _eventBus = eventBus;
    }

    private string ClassificationsTable => $"{_schema}.action_safety_classifications";
    private string ActionsTable => $"{_schema}.governed_action_records";

    // ── Initialization ──────────────────────────────────────────

    private Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return Task.CompletedTask;
        // Table creation is managed by DbUp migrations in ArchonAI.Migrations.
        // See Scripts/017_create_action_safety.sql
        _initialized = true;
        return Task.CompletedTask;
    }

    private async Task SeedDefaultClassificationsAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var countCmd = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM {ClassificationsTable}", conn);
        var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));
        if (count > 0) return;

        var seeds = new (string ActionType, ReversibilityLevel Rev, bool RollbackSupported, RollbackStrategy Strategy, TimeSpan? Window, string? Compensation, string? Notes)[]
        {
            ("data.read", ReversibilityLevel.Reversible, false, RollbackStrategy.None, null, null, "Read-only action, inherently safe."),
            ("notification.send", ReversibilityLevel.Irreversible, false, RollbackStrategy.None, null, null, "Notifications cannot be unsent."),
            ("workflow.execute", ReversibilityLevel.Reversible, true, RollbackStrategy.Automatic, TimeSpan.FromHours(1), null, "Workflow state can be reverted within window."),
            ("decision.execute", ReversibilityLevel.Compensatable, true, RollbackStrategy.Compensation, null, "Create a reversing decision with opposite parameters.", "Decisions can be offset by compensating decisions."),
            ("connector.send", ReversibilityLevel.Irreversible, false, RollbackStrategy.None, null, null, "External system calls cannot be recalled."),
            ("connector.disconnect", ReversibilityLevel.Reversible, true, RollbackStrategy.Automatic, TimeSpan.FromMinutes(30), null, "Reconnection is automatic."),
            ("strategy.override", ReversibilityLevel.Compensatable, true, RollbackStrategy.ManualTrigger, TimeSpan.FromHours(24), "Revert to previous strategy version.", "Strategy overrides require manual revert."),
            ("policy.delete", ReversibilityLevel.Compensatable, true, RollbackStrategy.Compensation, null, "Recreate deleted policy from audit log.", "Deleted policies can be restored from audit trail."),
            ("workflow.cancel", ReversibilityLevel.Reversible, true, RollbackStrategy.Automatic, TimeSpan.FromMinutes(15), null, "Cancelled workflows can be restarted."),
            ("rbac.role.delete", ReversibilityLevel.Compensatable, true, RollbackStrategy.ManualTrigger, TimeSpan.FromHours(1), "Recreate role and reassign permissions from audit log.", "Role deletion requires manual recreation."),
        };

        foreach (var seed in seeds)
        {
            await using var insertCmd = new NpgsqlCommand($@"
                INSERT INTO {ClassificationsTable}
                    (id, action_type, reversibility, rollback_supported, rollback_strategy,
                     rollback_window_ticks, compensation_description, operator_notes,
                     classified_by, classified_at_utc)
                VALUES
                    (@id, @actionType, @reversibility, @rollbackSupported, @rollbackStrategy,
                     @rollbackWindowTicks, @compensationDescription, @operatorNotes,
                     @classifiedBy, @classifiedAtUtc)
            ", conn);

            insertCmd.Parameters.AddWithValue("id", Guid.NewGuid());
            insertCmd.Parameters.AddWithValue("actionType", seed.ActionType);
            insertCmd.Parameters.AddWithValue("reversibility", (int)seed.Rev);
            insertCmd.Parameters.AddWithValue("rollbackSupported", seed.RollbackSupported);
            insertCmd.Parameters.AddWithValue("rollbackStrategy", (int)seed.Strategy);
            insertCmd.Parameters.AddWithValue("rollbackWindowTicks", (object?)seed.Window?.Ticks ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("compensationDescription", (object?)seed.Compensation ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("operatorNotes", (object?)seed.Notes ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("classifiedBy", "system");
            insertCmd.Parameters.AddWithValue("classifiedAtUtc", DateTimeOffset.UtcNow);

            await insertCmd.ExecuteNonQueryAsync(ct);
        }

        _logger.LogInformation("Seeded {Count} default action safety classifications.", seeds.Length);
    }

    // ── GetClassificationAsync ──────────────────────────────────

    public async Task<ActionSafetyClassification> GetClassificationAsync(
        string actionType, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {ClassificationsTable} WHERE action_type = @actionType", conn);
        cmd.Parameters.AddWithValue("actionType", actionType);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            return MapClassification(reader);

        // Return a default unknown classification
        return new ActionSafetyClassification(
            Guid.NewGuid(), actionType,
            ReversibilityLevel.Irreversible, false, RollbackStrategy.None,
            null, null, "Unclassified action type — treated as irreversible.",
            "system", DateTimeOffset.UtcNow);
    }

    // ── SetClassificationAsync ──────────────────────────────────

    public async Task<ActionSafetyClassification> SetClassificationAsync(
        ActionSafetyClassification classification, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {ClassificationsTable}
                (id, action_type, reversibility, rollback_supported, rollback_strategy,
                 rollback_window_ticks, compensation_description, operator_notes,
                 classified_by, classified_at_utc)
            VALUES
                (@id, @actionType, @reversibility, @rollbackSupported, @rollbackStrategy,
                 @rollbackWindowTicks, @compensationDescription, @operatorNotes,
                 @classifiedBy, @classifiedAtUtc)
            ON CONFLICT (action_type) DO UPDATE SET
                reversibility = EXCLUDED.reversibility,
                rollback_supported = EXCLUDED.rollback_supported,
                rollback_strategy = EXCLUDED.rollback_strategy,
                rollback_window_ticks = EXCLUDED.rollback_window_ticks,
                compensation_description = EXCLUDED.compensation_description,
                operator_notes = EXCLUDED.operator_notes,
                classified_by = EXCLUDED.classified_by,
                classified_at_utc = EXCLUDED.classified_at_utc
        ", conn);

        cmd.Parameters.AddWithValue("id", classification.Id);
        cmd.Parameters.AddWithValue("actionType", classification.ActionType);
        cmd.Parameters.AddWithValue("reversibility", (int)classification.Reversibility);
        cmd.Parameters.AddWithValue("rollbackSupported", classification.RollbackSupported);
        cmd.Parameters.AddWithValue("rollbackStrategy", (int)classification.RollbackStrategy);
        cmd.Parameters.AddWithValue("rollbackWindowTicks", (object?)classification.RollbackWindow?.Ticks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("compensationDescription", (object?)classification.CompensationDescription ?? DBNull.Value);
        cmd.Parameters.AddWithValue("operatorNotes", (object?)classification.OperatorNotes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("classifiedBy", classification.ClassifiedBy);
        cmd.Parameters.AddWithValue("classifiedAtUtc", classification.ClassifiedAtUtc);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Safety classification set for action type '{ActionType}'.", classification.ActionType);
        return classification;
    }

    // ── ListClassificationsAsync ────────────────────────────────

    public async Task<IReadOnlyList<ActionSafetyClassification>> ListClassificationsAsync(
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {ClassificationsTable} ORDER BY action_type", conn);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ActionSafetyClassification>();
        while (await reader.ReadAsync(ct))
            results.Add(MapClassification(reader));
        return results;
    }

    // ── RecordActionAsync ───────────────────────────────────────

    public async Task<GovernedActionRecord> RecordActionAsync(
        GovernedActionRecord action, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {ActionsTable}
                (id, tenant_id, decision_id, workflow_id, approval_gate_id,
                 action_type, description, safety_classification, status,
                 executed_by, executed_at_utc, rollback_history, compensation_outcome, updated_at_utc)
            VALUES
                (@id, @tenantId, @decisionId, @workflowId, @approvalGateId,
                 @actionType, @description, @safetyClassification::jsonb, @status,
                 @executedBy, @executedAtUtc, @rollbackHistory::jsonb, @compensationOutcome, @updatedAtUtc)
        ", conn);

        cmd.Parameters.AddWithValue("id", action.Id);
        cmd.Parameters.AddWithValue("tenantId", action.TenantId);
        cmd.Parameters.AddWithValue("decisionId", (object?)action.DecisionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("workflowId", (object?)action.WorkflowId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("approvalGateId", (object?)action.ApprovalGateId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actionType", action.ActionType);
        cmd.Parameters.AddWithValue("description", action.Description);
        cmd.Parameters.AddWithValue("safetyClassification", JsonSerializer.Serialize(action.SafetyClassification, JsonOpts));
        cmd.Parameters.AddWithValue("status", (int)action.Status);
        cmd.Parameters.AddWithValue("executedBy", action.ExecutedBy);
        cmd.Parameters.AddWithValue("executedAtUtc", action.ExecutedAtUtc);
        cmd.Parameters.AddWithValue("rollbackHistory", JsonSerializer.Serialize(action.RollbackHistory, JsonOpts));
        cmd.Parameters.AddWithValue("compensationOutcome", (object?)action.CompensationOutcome ?? DBNull.Value);
        cmd.Parameters.AddWithValue("updatedAtUtc", action.UpdatedAtUtc);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Governed action {ActionId} recorded (type={ActionType}).", action.Id, action.ActionType);
        return action;
    }

    // ── GetActionAsync ──────────────────────────────────────────

    public async Task<GovernedActionRecord?> GetActionAsync(
        Guid actionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            $"SELECT * FROM {ActionsTable} WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", actionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapAction(reader) : null;
    }

    // ── ListActionsAsync ────────────────────────────────────────

    public async Task<IReadOnlyList<GovernedActionRecord>> ListActionsAsync(
        Guid tenantId, int limit = 50, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {ActionsTable}
            WHERE tenant_id = @tenantId
            ORDER BY executed_at_utc DESC
            LIMIT @limit
        ", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<GovernedActionRecord>();
        while (await reader.ReadAsync(ct))
            results.Add(MapAction(reader));
        return results;
    }

    // ── AttemptRollbackAsync ────────────────────────────────────

    public async Task<GovernedActionRecord> AttemptRollbackAsync(
        Guid actionId, string initiatedBy, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var action = await GetActionAsync(actionId, ct)
            ?? throw new InvalidOperationException($"Governed action '{actionId}' not found.");

        var classification = action.SafetyClassification;
        var now = DateTimeOffset.UtcNow;
        var attemptId = Guid.NewGuid();

        // ── Block if irreversible ───────────────────────────────
        if (classification.Reversibility == ReversibilityLevel.Irreversible)
        {
            var blockedAttempt = new RollbackAttempt(
                attemptId, actionId, initiatedBy,
                RollbackAttemptStatus.Blocked,
                "Action is classified as irreversible.", null,
                now, now);
            var history = action.RollbackHistory.ToList();
            history.Add(blockedAttempt);
            var updated = action with
            {
                Status = GovernedActionStatus.Irreversible,
                RollbackHistory = history,
                UpdatedAtUtc = now,
            };
            await UpdateActionAsync(updated, ct);
            return updated;
        }

        // ── Check rollback window expiration ────────────────────
        if (classification.RollbackWindow.HasValue)
        {
            var deadline = action.ExecutedAtUtc + classification.RollbackWindow.Value;
            if (now > deadline)
            {
                var expiredAttempt = new RollbackAttempt(
                    attemptId, actionId, initiatedBy,
                    RollbackAttemptStatus.Blocked,
                    $"Rollback window expired at {deadline:O}.", null,
                    now, now);
                var history = action.RollbackHistory.ToList();
                history.Add(expiredAttempt);
                var updated = action with
                {
                    Status = GovernedActionStatus.RollbackWindowExpired,
                    RollbackHistory = history,
                    UpdatedAtUtc = now,
                };
                await UpdateActionAsync(updated, ct);
                return updated;
            }
        }

        // ── Block if already rolled back ────────────────────────
        if (action.Status is GovernedActionStatus.RolledBack
            or GovernedActionStatus.CompensationApplied)
        {
            var alreadyAttempt = new RollbackAttempt(
                attemptId, actionId, initiatedBy,
                RollbackAttemptStatus.Blocked,
                "Action has already been rolled back or compensated.", null,
                now, now);
            var history = action.RollbackHistory.ToList();
            history.Add(alreadyAttempt);
            var updated = action with
            {
                RollbackHistory = history,
                UpdatedAtUtc = now,
            };
            await UpdateActionAsync(updated, ct);
            return updated;
        }

        // ── Execute rollback ────────────────────────────────────
        var rollbackHistory = action.RollbackHistory.ToList();
        var attempt = new RollbackAttempt(
            attemptId, actionId, initiatedBy,
            RollbackAttemptStatus.InProgress,
            "Rollback initiated.", null,
            now, null);
        rollbackHistory.Add(attempt);

        var inProgressAction = action with
        {
            Status = GovernedActionStatus.RollbackInProgress,
            RollbackHistory = rollbackHistory,
            UpdatedAtUtc = now,
        };
        await UpdateActionAsync(inProgressAction, ct);

        try
        {
            // Simulate rollback execution (in a real system this would call subsystem-specific rollback)
            GovernedActionStatus finalStatus;
            string? compensationOutcome = null;

            if (classification.RollbackStrategy == RollbackStrategy.Compensation)
            {
                finalStatus = GovernedActionStatus.CompensationApplied;
                compensationOutcome = classification.CompensationDescription
                    ?? "Compensation applied successfully.";
            }
            else
            {
                finalStatus = GovernedActionStatus.RolledBack;
            }

            var completedNow = DateTimeOffset.UtcNow;
            var completedAttempt = attempt with
            {
                Status = RollbackAttemptStatus.Succeeded,
                Detail = classification.RollbackStrategy == RollbackStrategy.Compensation
                    ? "Compensation applied."
                    : "Rollback completed.",
                CompletedAtUtc = completedNow,
            };

            rollbackHistory[^1] = completedAttempt;

            var finalAction = inProgressAction with
            {
                Status = finalStatus,
                RollbackHistory = rollbackHistory,
                CompensationOutcome = compensationOutcome,
                UpdatedAtUtc = completedNow,
            };
            await UpdateActionAsync(finalAction, ct);

            // Publish proof event
            await _eventBus.PublishAsync(new SystemEvent(
                Guid.NewGuid(), "ActionSafety.RollbackCompleted",
                nameof(PostgresActionSafetyStore), actionId,
                new Dictionary<string, string>
                {
                    ["actionId"] = actionId.ToString(),
                    ["status"] = finalStatus.ToString(),
                    ["initiatedBy"] = initiatedBy,
                },
                completedNow), ct);

            _logger.LogInformation("Rollback completed for action {ActionId} (status={Status}).", actionId, finalStatus);
            return finalAction;
        }
        catch (Exception ex)
        {
            var failedNow = DateTimeOffset.UtcNow;
            var failedAttempt = attempt with
            {
                Status = RollbackAttemptStatus.Failed,
                Error = ex.Message,
                CompletedAtUtc = failedNow,
            };

            rollbackHistory[^1] = failedAttempt;

            var failedStatus = classification.RollbackStrategy == RollbackStrategy.Compensation
                ? GovernedActionStatus.CompensationFailed
                : GovernedActionStatus.RollbackFailed;

            var failedAction = inProgressAction with
            {
                Status = failedStatus,
                RollbackHistory = rollbackHistory,
                UpdatedAtUtc = failedNow,
            };
            await UpdateActionAsync(failedAction, ct);

            _logger.LogError(ex, "Rollback failed for action {ActionId}.", actionId);
            return failedAction;
        }
    }

    // ── GetRollbackSummaryAsync ─────────────────────────────────

    public async Task<RollbackSummary> GetRollbackSummaryAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var actions = await ListActionsAsync(tenantId, 1000, ct);

        var reversible = actions.Count(a => a.SafetyClassification.Reversibility == ReversibilityLevel.Reversible);
        var compensatable = actions.Count(a => a.SafetyClassification.Reversibility == ReversibilityLevel.Compensatable);
        var irreversible = actions.Count(a => a.SafetyClassification.Reversibility == ReversibilityLevel.Irreversible);

        var allAttempts = actions.SelectMany(a => a.RollbackHistory).ToList();
        var attempted = allAttempts.Count;
        var succeeded = allAttempts.Count(a => a.Status == RollbackAttemptStatus.Succeeded);
        var failed = allAttempts.Count(a => a.Status == RollbackAttemptStatus.Failed);

        var withinWindow = actions.Count(a =>
            a.SafetyClassification.RollbackWindow.HasValue
            && DateTimeOffset.UtcNow <= a.ExecutedAtUtc + a.SafetyClassification.RollbackWindow.Value
            && a.Status is GovernedActionStatus.Executed or GovernedActionStatus.RollbackEligible);

        var expired = actions.Count(a => a.Status == GovernedActionStatus.RollbackWindowExpired);

        return new RollbackSummary(
            tenantId, actions.Count, reversible, compensatable, irreversible,
            attempted, succeeded, failed, withinWindow, expired);
    }

    // ── DB Helpers ──────────────────────────────────────────────

    private async Task UpdateActionAsync(GovernedActionRecord action, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            UPDATE {ActionsTable}
            SET status = @status,
                rollback_history = @rollbackHistory::jsonb,
                compensation_outcome = @compensationOutcome,
                updated_at_utc = @updatedAtUtc
            WHERE id = @id
        ", conn);

        cmd.Parameters.AddWithValue("status", (int)action.Status);
        cmd.Parameters.AddWithValue("rollbackHistory", JsonSerializer.Serialize(action.RollbackHistory, JsonOpts));
        cmd.Parameters.AddWithValue("compensationOutcome", (object?)action.CompensationOutcome ?? DBNull.Value);
        cmd.Parameters.AddWithValue("updatedAtUtc", action.UpdatedAtUtc);
        cmd.Parameters.AddWithValue("id", action.Id);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Row Mappers ─────────────────────────────────────────────

    private static ActionSafetyClassification MapClassification(NpgsqlDataReader reader)
    {
        var windowOrd = reader.GetOrdinal("rollback_window_ticks");
        TimeSpan? rollbackWindow = reader.IsDBNull(windowOrd)
            ? null
            : TimeSpan.FromTicks(reader.GetInt64(windowOrd));

        return new ActionSafetyClassification(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("action_type")),
            (ReversibilityLevel)reader.GetInt32(reader.GetOrdinal("reversibility")),
            reader.GetBoolean(reader.GetOrdinal("rollback_supported")),
            (RollbackStrategy)reader.GetInt32(reader.GetOrdinal("rollback_strategy")),
            rollbackWindow,
            reader.IsDBNull(reader.GetOrdinal("compensation_description")) ? null : reader.GetString(reader.GetOrdinal("compensation_description")),
            reader.IsDBNull(reader.GetOrdinal("operator_notes")) ? null : reader.GetString(reader.GetOrdinal("operator_notes")),
            reader.GetString(reader.GetOrdinal("classified_by")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("classified_at_utc")));
    }

    private static GovernedActionRecord MapAction(NpgsqlDataReader reader)
    {
        return new GovernedActionRecord(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetGuid(reader.GetOrdinal("tenant_id")),
            reader.IsDBNull(reader.GetOrdinal("decision_id")) ? null : reader.GetGuid(reader.GetOrdinal("decision_id")),
            reader.IsDBNull(reader.GetOrdinal("workflow_id")) ? null : reader.GetGuid(reader.GetOrdinal("workflow_id")),
            reader.IsDBNull(reader.GetOrdinal("approval_gate_id")) ? null : reader.GetGuid(reader.GetOrdinal("approval_gate_id")),
            reader.GetString(reader.GetOrdinal("action_type")),
            reader.GetString(reader.GetOrdinal("description")),
            JsonSerializer.Deserialize<ActionSafetyClassification>(reader.GetString(reader.GetOrdinal("safety_classification")), JsonOpts)!,
            (GovernedActionStatus)reader.GetInt32(reader.GetOrdinal("status")),
            reader.GetString(reader.GetOrdinal("executed_by")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("executed_at_utc")),
            JsonSerializer.Deserialize<List<RollbackAttempt>>(reader.GetString(reader.GetOrdinal("rollback_history")), JsonOpts) ?? new(),
            reader.IsDBNull(reader.GetOrdinal("compensation_outcome")) ? null : reader.GetString(reader.GetOrdinal("compensation_outcome")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at_utc")));
    }
}

using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Governance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresGovernanceStore : IGovernanceService
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresGovernanceStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private string GatesTable => $"{_schema}.approval_gates";
    private string PoliciesTable => $"{_schema}.approval_policies";
    private string AuditTable => $"{_schema}.approval_audit_entries";

    public PostgresGovernanceStore(IOptions<PersistenceOptions> options, ILogger<PostgresGovernanceStore> logger)
    {
        _connectionString = options.Value.ConnectionStringHardened ?? throw new InvalidOperationException("Persistence connection string is not configured.");
        _schema = options.Value.Schema;
        _logger = logger;
    }

    public async Task<ApprovalGate> RequestApprovalAsync(
        string actionType, string resourceId, string tenantId,
        string requestedBy, string justification,
        string? actionPayload = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Deduplication: return existing pending gate for same (actionType, resourceId, tenant)
        var dedupSql = $"""
            SELECT id, action_type, resource_id, tenant_id, requested_by, justification, status,
                   reviewed_by, review_notes, requested_at_utc, reviewed_at_utc,
                   action_payload, execution_status, execution_error, executed_at_utc
            FROM {GatesTable}
            WHERE status = @pendingStatus
              AND action_type = @actionType
              AND resource_id = @resourceId
              AND LOWER(tenant_id) = LOWER(@tenantId)
            LIMIT 1;
            """;

        await using var dedupCmd = new NpgsqlCommand(dedupSql, connection);
        dedupCmd.Parameters.AddWithValue("pendingStatus", (int)ApprovalStatus.Pending);
        dedupCmd.Parameters.AddWithValue("actionType", actionType);
        dedupCmd.Parameters.AddWithValue("resourceId", resourceId);
        dedupCmd.Parameters.AddWithValue("tenantId", tenantId);

        await using var dedupReader = await dedupCmd.ExecuteReaderAsync(ct);
        if (await dedupReader.ReadAsync(ct))
        {
            var existing = ReadApprovalGate(dedupReader);
            return existing;
        }
        await dedupReader.CloseAsync();

        // Create new gate
        var gate = new ApprovalGate(
            Id: Guid.NewGuid(),
            ActionType: actionType,
            ResourceId: resourceId,
            TenantId: tenantId,
            RequestedBy: requestedBy,
            Justification: justification,
            Status: ApprovalStatus.Pending,
            ReviewedBy: null,
            ReviewNotes: null,
            RequestedAtUtc: DateTimeOffset.UtcNow,
            ReviewedAtUtc: null)
        {
            ActionPayload = actionPayload
        };

        var sql = $"""
            INSERT INTO {GatesTable}
            (id, action_type, resource_id, tenant_id, requested_by, justification, status,
             reviewed_by, review_notes, requested_at_utc, reviewed_at_utc,
             action_payload, execution_status, execution_error, executed_at_utc)
            VALUES
            (@id, @actionType, @resourceId, @tenantId, @requestedBy, @justification, @status,
             @reviewedBy, @reviewNotes, @requestedAtUtc, @reviewedAtUtc,
             @actionPayload, @executionStatus, @executionError, @executedAtUtc);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", gate.Id);
        command.Parameters.AddWithValue("actionType", actionType);
        command.Parameters.AddWithValue("resourceId", resourceId);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("requestedBy", requestedBy);
        command.Parameters.AddWithValue("justification", justification);
        command.Parameters.AddWithValue("status", (int)gate.Status);
        command.Parameters.AddWithValue("reviewedBy", (object?)gate.ReviewedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("reviewNotes", (object?)gate.ReviewNotes ?? DBNull.Value);
        command.Parameters.AddWithValue("requestedAtUtc", gate.RequestedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("reviewedAtUtc", (object?)gate.ReviewedAtUtc?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("actionPayload", (object?)gate.ActionPayload ?? DBNull.Value);
        command.Parameters.AddWithValue("executionStatus", (int)gate.ExecutionStatus);
        command.Parameters.AddWithValue("executionError", (object?)gate.ExecutionError ?? DBNull.Value);
        command.Parameters.AddWithValue("executedAtUtc", (object?)gate.ExecutedAtUtc?.UtcDateTime ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(ct);

        return gate;
    }

    public async Task<ApprovalGate?> GetApprovalAsync(Guid gateId, string tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = $"""
            SELECT id, action_type, resource_id, tenant_id, requested_by, justification, status,
                   reviewed_by, review_notes, requested_at_utc, reviewed_at_utc,
                   action_payload, execution_status, execution_error, executed_at_utc
            FROM {GatesTable}
            WHERE id = @id AND LOWER(tenant_id) = LOWER(@tenantId);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", gateId);
        command.Parameters.AddWithValue("tenantId", tenantId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadApprovalGate(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<ApprovalGate>> ListPendingApprovalsAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = $"""
            SELECT id, action_type, resource_id, tenant_id, requested_by, justification, status,
                   reviewed_by, review_notes, requested_at_utc, reviewed_at_utc,
                   action_payload, execution_status, execution_error, executed_at_utc
            FROM {GatesTable}
            WHERE tenant_id = @tenantId AND status = @pendingStatus
            ORDER BY requested_at_utc DESC;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("pendingStatus", (int)ApprovalStatus.Pending);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<ApprovalGate>();
        while (await reader.ReadAsync(ct))
        {
            result.Add(ReadApprovalGate(reader));
        }

        return result;
    }

    public async Task<ApprovalGate> ReviewApprovalAsync(
        Guid gateId, string tenantId, string reviewedBy, string reviewerRole,
        bool approve, string? notes, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Fetch existing gate
        var fetchSql = $"""
            SELECT id, action_type, resource_id, tenant_id, requested_by, justification, status,
                   reviewed_by, review_notes, requested_at_utc, reviewed_at_utc,
                   action_payload, execution_status, execution_error, executed_at_utc
            FROM {GatesTable}
            WHERE id = @id;
            """;

        await using var fetchCmd = new NpgsqlCommand(fetchSql, connection);
        fetchCmd.Parameters.AddWithValue("id", gateId);

        await using var fetchReader = await fetchCmd.ExecuteReaderAsync(ct);
        if (!await fetchReader.ReadAsync(ct))
            throw new KeyNotFoundException($"Approval gate {gateId} not found.");

        var gate = ReadApprovalGate(fetchReader);
        await fetchReader.CloseAsync();

        // Enforce tenant isolation
        if (!string.Equals(gate.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            throw new KeyNotFoundException($"Approval gate {gateId} not found.");

        if (gate.Status != ApprovalStatus.Pending)
            throw new InvalidOperationException($"Approval gate {gateId} is already {gate.Status}.");

        // Check policy constraints
        var policySql = $"""
            SELECT id, action_type, description, required_approver_role, require_separation_of_duties, is_enabled, created_at_utc
            FROM {PoliciesTable}
            WHERE action_type = @actionType AND is_enabled = true
            LIMIT 1;
            """;

        await using var policyCmd = new NpgsqlCommand(policySql, connection);
        policyCmd.Parameters.AddWithValue("actionType", gate.ActionType);

        await using var policyReader = await policyCmd.ExecuteReaderAsync(ct);
        ApprovalPolicy? policy = null;
        if (await policyReader.ReadAsync(ct))
        {
            policy = ReadApprovalPolicy(policyReader);
        }
        await policyReader.CloseAsync();

        // Enforce RequiredApproverRole
        if (policy is not null
            && !string.Equals(reviewerRole, policy.RequiredApproverRole, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Reviewer role '{reviewerRole}' does not satisfy required approver role '{policy.RequiredApproverRole}'.");
        }

        // Separation of duties
        if (policy?.RequireSeparationOfDuties == true
            && string.Equals(gate.RequestedBy, reviewedBy, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Separation of duties violation: the requester cannot approve their own request.");
        }

        var status = approve ? ApprovalStatus.Approved : ApprovalStatus.Denied;
        var reviewedAtUtc = DateTimeOffset.UtcNow;

        var updateSql = $"""
            UPDATE {GatesTable}
            SET status = @status, reviewed_by = @reviewedBy, review_notes = @reviewNotes, reviewed_at_utc = @reviewedAtUtc
            WHERE id = @id;
            """;

        await using var updateCmd = new NpgsqlCommand(updateSql, connection);
        updateCmd.Parameters.AddWithValue("id", gateId);
        updateCmd.Parameters.AddWithValue("status", (int)status);
        updateCmd.Parameters.AddWithValue("reviewedBy", reviewedBy);
        updateCmd.Parameters.AddWithValue("reviewNotes", (object?)notes ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("reviewedAtUtc", reviewedAtUtc.UtcDateTime);

        await updateCmd.ExecuteNonQueryAsync(ct);

        // Record audit entry
        var auditSql = $"""
            INSERT INTO {AuditTable}
            (id, approval_gate_id, action_type, tenant_id, requested_by, reviewed_by, outcome, occurred_at_utc)
            VALUES
            (@auditId, @approvalGateId, @actionType, @tenantId, @requestedBy, @auditReviewedBy, @outcome, @occurredAtUtc);
            """;

        await using var auditCmd = new NpgsqlCommand(auditSql, connection);
        auditCmd.Parameters.AddWithValue("auditId", Guid.NewGuid());
        auditCmd.Parameters.AddWithValue("approvalGateId", gateId);
        auditCmd.Parameters.AddWithValue("actionType", gate.ActionType);
        auditCmd.Parameters.AddWithValue("tenantId", gate.TenantId);
        auditCmd.Parameters.AddWithValue("requestedBy", gate.RequestedBy);
        auditCmd.Parameters.AddWithValue("auditReviewedBy", reviewedBy);
        auditCmd.Parameters.AddWithValue("outcome", (int)status);
        auditCmd.Parameters.AddWithValue("occurredAtUtc", reviewedAtUtc.UtcDateTime);

        await auditCmd.ExecuteNonQueryAsync(ct);

        var reviewed = gate with
        {
            Status = status,
            ReviewedBy = reviewedBy,
            ReviewNotes = notes,
            ReviewedAtUtc = reviewedAtUtc,
        };

        return reviewed;
    }

    public async Task<ApprovalGate> RecordExecutionResultAsync(
        Guid gateId, GateExecutionStatus status, string? error, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Fetch existing gate
        var fetchSql = $"""
            SELECT id, action_type, resource_id, tenant_id, requested_by, justification, status,
                   reviewed_by, review_notes, requested_at_utc, reviewed_at_utc,
                   action_payload, execution_status, execution_error, executed_at_utc
            FROM {GatesTable}
            WHERE id = @id;
            """;

        await using var fetchCmd = new NpgsqlCommand(fetchSql, connection);
        fetchCmd.Parameters.AddWithValue("id", gateId);

        await using var fetchReader = await fetchCmd.ExecuteReaderAsync(ct);
        if (!await fetchReader.ReadAsync(ct))
            throw new KeyNotFoundException($"Approval gate {gateId} not found.");

        var gate = ReadApprovalGate(fetchReader);
        await fetchReader.CloseAsync();

        var executedAtUtc = DateTimeOffset.UtcNow;

        var updateSql = $"""
            UPDATE {GatesTable}
            SET execution_status = @executionStatus, execution_error = @executionError, executed_at_utc = @executedAtUtc
            WHERE id = @id;
            """;

        await using var updateCmd = new NpgsqlCommand(updateSql, connection);
        updateCmd.Parameters.AddWithValue("id", gateId);
        updateCmd.Parameters.AddWithValue("executionStatus", (int)status);
        updateCmd.Parameters.AddWithValue("executionError", (object?)error ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("executedAtUtc", executedAtUtc.UtcDateTime);

        await updateCmd.ExecuteNonQueryAsync(ct);

        return gate with
        {
            ExecutionStatus = status,
            ExecutionError = error,
            ExecutedAtUtc = executedAtUtc,
        };
    }

    public async Task<IReadOnlyList<ApprovalPolicy>> ListApprovalPoliciesAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = $"""
            SELECT id, action_type, description, required_approver_role, require_separation_of_duties, is_enabled, created_at_utc
            FROM {PoliciesTable}
            ORDER BY action_type;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<ApprovalPolicy>();
        while (await reader.ReadAsync(ct))
        {
            result.Add(ReadApprovalPolicy(reader));
        }

        return result;
    }

    public async Task<ApprovalPolicy> CreateApprovalPolicyAsync(
        string actionType, string description, string requiredApproverRole,
        bool requireSeparationOfDuties, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var policy = new ApprovalPolicy(
            Id: Guid.NewGuid(),
            ActionType: actionType,
            Description: description,
            RequiredApproverRole: requiredApproverRole,
            RequireSeparationOfDuties: requireSeparationOfDuties,
            IsEnabled: true,
            CreatedAtUtc: DateTimeOffset.UtcNow);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = $"""
            INSERT INTO {PoliciesTable}
            (id, action_type, description, required_approver_role, require_separation_of_duties, is_enabled, created_at_utc)
            VALUES
            (@id, @actionType, @description, @requiredApproverRole, @requireSeparationOfDuties, @isEnabled, @createdAtUtc);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", policy.Id);
        command.Parameters.AddWithValue("actionType", actionType);
        command.Parameters.AddWithValue("description", description);
        command.Parameters.AddWithValue("requiredApproverRole", requiredApproverRole);
        command.Parameters.AddWithValue("requireSeparationOfDuties", requireSeparationOfDuties);
        command.Parameters.AddWithValue("isEnabled", true);
        command.Parameters.AddWithValue("createdAtUtc", policy.CreatedAtUtc.UtcDateTime);

        await command.ExecuteNonQueryAsync(ct);

        return policy;
    }

    public async Task<bool> RequiresApprovalAsync(string actionType, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = $"SELECT COUNT(*) FROM {PoliciesTable} WHERE action_type = @actionType AND is_enabled = true;";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("actionType", actionType);

        var count = Convert.ToInt64(await command.ExecuteScalarAsync(ct));
        return count > 0;
    }

    public async Task<IReadOnlyList<ApprovalAuditEntry>> GetApprovalHistoryAsync(
        string? tenantId, string? actionType, int limit, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var whereClauses = new List<string>();
        var parameters = new List<NpgsqlParameter>();

        if (tenantId is not null)
        {
            whereClauses.Add("tenant_id = @tenantId");
            parameters.Add(new NpgsqlParameter("tenantId", tenantId));
        }
        if (actionType is not null)
        {
            whereClauses.Add("action_type = @actionType");
            parameters.Add(new NpgsqlParameter("actionType", actionType));
        }

        var whereClause = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

        var sql = $"""
            SELECT id, approval_gate_id, action_type, tenant_id, requested_by, reviewed_by, outcome, occurred_at_utc
            FROM {AuditTable}
            {whereClause}
            ORDER BY occurred_at_utc DESC
            LIMIT @limit;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var p in parameters) command.Parameters.Add(p);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 1000));

        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<ApprovalAuditEntry>();
        while (await reader.ReadAsync(ct))
        {
            var reviewedByOrdinal = reader.GetOrdinal("reviewed_by");
            result.Add(new ApprovalAuditEntry(
                Id: reader.GetGuid(reader.GetOrdinal("id")),
                ApprovalGateId: reader.GetGuid(reader.GetOrdinal("approval_gate_id")),
                ActionType: reader.GetString(reader.GetOrdinal("action_type")),
                TenantId: reader.GetString(reader.GetOrdinal("tenant_id")),
                RequestedBy: reader.GetString(reader.GetOrdinal("requested_by")),
                ReviewedBy: reader.IsDBNull(reviewedByOrdinal) ? null : reader.GetString(reviewedByOrdinal),
                Outcome: (ApprovalStatus)reader.GetInt32(reader.GetOrdinal("outcome")),
                OccurredAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("occurred_at_utc")), TimeSpan.Zero)));
        }

        return result;
    }

    private static ApprovalGate ReadApprovalGate(NpgsqlDataReader reader)
    {
        var reviewedByOrdinal = reader.GetOrdinal("reviewed_by");
        var reviewNotesOrdinal = reader.GetOrdinal("review_notes");
        var reviewedAtUtcOrdinal = reader.GetOrdinal("reviewed_at_utc");
        var actionPayloadOrdinal = reader.GetOrdinal("action_payload");
        var executionErrorOrdinal = reader.GetOrdinal("execution_error");
        var executedAtUtcOrdinal = reader.GetOrdinal("executed_at_utc");

        return new ApprovalGate(
            Id: reader.GetGuid(reader.GetOrdinal("id")),
            ActionType: reader.GetString(reader.GetOrdinal("action_type")),
            ResourceId: reader.GetString(reader.GetOrdinal("resource_id")),
            TenantId: reader.GetString(reader.GetOrdinal("tenant_id")),
            RequestedBy: reader.GetString(reader.GetOrdinal("requested_by")),
            Justification: reader.GetString(reader.GetOrdinal("justification")),
            Status: (ApprovalStatus)reader.GetInt32(reader.GetOrdinal("status")),
            ReviewedBy: reader.IsDBNull(reviewedByOrdinal) ? null : reader.GetString(reviewedByOrdinal),
            ReviewNotes: reader.IsDBNull(reviewNotesOrdinal) ? null : reader.GetString(reviewNotesOrdinal),
            RequestedAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("requested_at_utc")), TimeSpan.Zero),
            ReviewedAtUtc: reader.IsDBNull(reviewedAtUtcOrdinal) ? null : new DateTimeOffset(reader.GetFieldValue<DateTime>(reviewedAtUtcOrdinal), TimeSpan.Zero))
        {
            ActionPayload = reader.IsDBNull(actionPayloadOrdinal) ? null : reader.GetString(actionPayloadOrdinal),
            ExecutionStatus = (GateExecutionStatus)reader.GetInt32(reader.GetOrdinal("execution_status")),
            ExecutionError = reader.IsDBNull(executionErrorOrdinal) ? null : reader.GetString(executionErrorOrdinal),
            ExecutedAtUtc = reader.IsDBNull(executedAtUtcOrdinal) ? null : new DateTimeOffset(reader.GetFieldValue<DateTime>(executedAtUtcOrdinal), TimeSpan.Zero),
        };
    }

    private static ApprovalPolicy ReadApprovalPolicy(NpgsqlDataReader reader)
    {
        return new ApprovalPolicy(
            Id: reader.GetGuid(reader.GetOrdinal("id")),
            ActionType: reader.GetString(reader.GetOrdinal("action_type")),
            Description: reader.GetString(reader.GetOrdinal("description")),
            RequiredApproverRole: reader.GetString(reader.GetOrdinal("required_approver_role")),
            RequireSeparationOfDuties: reader.GetBoolean(reader.GetOrdinal("require_separation_of_duties")),
            IsEnabled: reader.GetBoolean(reader.GetOrdinal("is_enabled")),
            CreatedAtUtc: new DateTimeOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at_utc")), TimeSpan.Zero));
    }

    private async Task SeedDefaultPoliciesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var countSql = $"SELECT COUNT(*) FROM {PoliciesTable};";
        await using var countCmd = new NpgsqlCommand(countSql, connection);
        var count = Convert.ToInt64(await countCmd.ExecuteScalarAsync(cancellationToken));

        if (count > 0)
            return;

        var now = DateTimeOffset.UtcNow;
        var policies = new[]
        {
            new ApprovalPolicy(Guid.NewGuid(), "workflow.cancel", "Cancelling a running workflow",
                "Admin", true, true, now),
            new ApprovalPolicy(Guid.NewGuid(), "policy.delete", "Deleting a governance policy",
                "Admin", true, true, now),
            new ApprovalPolicy(Guid.NewGuid(), "rbac.role.delete", "Deleting an RBAC role",
                "Admin", false, true, now),
            new ApprovalPolicy(Guid.NewGuid(), "connector.disconnect", "Disconnecting an active integration",
                "Admin", true, true, now),
            new ApprovalPolicy(Guid.NewGuid(), "strategy.override", "Overriding an AI-selected strategy",
                "Admin", true, true, now),
        };

        foreach (var policy in policies)
        {
            var sql = $"""
                INSERT INTO {PoliciesTable}
                (id, action_type, description, required_approver_role, require_separation_of_duties, is_enabled, created_at_utc)
                VALUES
                (@id, @actionType, @description, @requiredApproverRole, @requireSeparationOfDuties, @isEnabled, @createdAtUtc);
                """;

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", policy.Id);
            command.Parameters.AddWithValue("actionType", policy.ActionType);
            command.Parameters.AddWithValue("description", policy.Description);
            command.Parameters.AddWithValue("requiredApproverRole", policy.RequiredApproverRole);
            command.Parameters.AddWithValue("requireSeparationOfDuties", policy.RequireSeparationOfDuties);
            command.Parameters.AddWithValue("isEnabled", policy.IsEnabled);
            command.Parameters.AddWithValue("createdAtUtc", policy.CreatedAtUtc.UtcDateTime);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        _logger.LogInformation("Seeded 5 default approval policies");
    }

    private Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return Task.CompletedTask;
        // Table creation is managed by DbUp migrations in ArchonAI.Migrations.
        // See Scripts/004_create_governance.sql
        _initialized = true;
        return Task.CompletedTask;
    }
}

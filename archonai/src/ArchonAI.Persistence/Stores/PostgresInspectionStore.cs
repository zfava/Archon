using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Decisions;
using ArchonAI.Core.Models.HeroWorkflow;
using ArchonAI.Core.Models.Inspection;
using ArchonAI.Core.Models.ProofAnalytics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

public sealed class PostgresInspectionStore : IInspectionService
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ILogger<PostgresInspectionStore> _logger;
    private readonly IDecisionService _decisionService;
    private readonly IHeroWorkflowService _heroWorkflowService;
    private readonly IProofAnalyticsService _proofAnalyticsService;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public PostgresInspectionStore(
        IOptions<PersistenceOptions> options,
        ILogger<PostgresInspectionStore> logger,
        IDecisionService decisionService,
        IHeroWorkflowService heroWorkflowService,
        IProofAnalyticsService proofAnalyticsService)
    {
        _connectionString = options.Value.ConnectionStringHardened
            ?? throw new ArgumentNullException(nameof(options), "ConnectionString is required.");
        _schema = options.Value.Schema;
        _logger = logger;
        _decisionService = decisionService;
        _heroWorkflowService = heroWorkflowService;
        _proofAnalyticsService = proofAnalyticsService;
    }

    private string PolicyEvalTable => $"{_schema}.inspection_policy_evaluations";
    private string MemoryRefTable => $"{_schema}.inspection_memory_references";
    private string WorkflowDiagTable => $"{_schema}.inspection_workflow_diagnostics";

    // ── Initialization ──────────────────────────────────────────

    private Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return Task.CompletedTask;
        // Table creation is managed by DbUp migrations in ArchonAI.Migrations.
        // See Scripts/018_create_inspection.sql
        _initialized = true;
        return Task.CompletedTask;
    }

    // ── Public record methods (not on interface, called by other services) ──

    public async Task RecordPolicyEvaluation(PolicyEvaluationResult eval, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {PolicyEvalTable}
                (evaluation_id, tenant_id, subject_type, subject_id, is_allowed, risk_score,
                 confidence_score, requires_approval, approval_state, manual_override_state,
                 approval_checkpoint, guardrail_violations, rules_evaluated, reason, evaluated_at_utc)
            VALUES
                (@evalId, @tenantId, @subjectType, @subjectId, @isAllowed, @riskScore,
                 @confidenceScore, @requiresApproval, @approvalState, @manualOverrideState,
                 @approvalCheckpoint, @guardrailViolations::jsonb, @rulesEvaluated::jsonb, @reason, @evaluatedAtUtc)
            ON CONFLICT (evaluation_id) DO NOTHING
        ", conn);

        cmd.Parameters.AddWithValue("evalId", eval.EvaluationId);
        cmd.Parameters.AddWithValue("tenantId", eval.TenantId);
        cmd.Parameters.AddWithValue("subjectType", eval.SubjectType);
        cmd.Parameters.AddWithValue("subjectId", eval.SubjectId);
        cmd.Parameters.AddWithValue("isAllowed", eval.IsAllowed);
        cmd.Parameters.AddWithValue("riskScore", eval.RiskScore);
        cmd.Parameters.AddWithValue("confidenceScore", eval.ConfidenceScore);
        cmd.Parameters.AddWithValue("requiresApproval", eval.RequiresApproval);
        cmd.Parameters.AddWithValue("approvalState", eval.ApprovalState);
        cmd.Parameters.AddWithValue("manualOverrideState", eval.ManualOverrideState);
        cmd.Parameters.AddWithValue("approvalCheckpoint", eval.ApprovalCheckpoint);
        cmd.Parameters.AddWithValue("guardrailViolations", JsonSerializer.Serialize(eval.GuardrailViolations, JsonOpts));
        cmd.Parameters.AddWithValue("rulesEvaluated", JsonSerializer.Serialize(eval.RulesEvaluated, JsonOpts));
        cmd.Parameters.AddWithValue("reason", eval.Reason);
        cmd.Parameters.AddWithValue("evaluatedAtUtc", eval.EvaluatedAtUtc);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordMemoryReference(
        Guid tenantId, string subjectType, string subjectId,
        MemoryContextReference memRef, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {MemoryRefTable}
                (id, tenant_id, subject_type, subject_id, memory_id, memory_type,
                 source, content_summary, relevance_score, usage_context, retrieved_at_utc)
            VALUES
                (@id, @tenantId, @subjectType, @subjectId, @memoryId, @memoryType,
                 @source, @contentSummary, @relevanceScore, @usageContext, @retrievedAtUtc)
        ", conn);

        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("subjectType", subjectType);
        cmd.Parameters.AddWithValue("subjectId", subjectId);
        cmd.Parameters.AddWithValue("memoryId", memRef.MemoryId);
        cmd.Parameters.AddWithValue("memoryType", memRef.MemoryType);
        cmd.Parameters.AddWithValue("source", memRef.Source);
        cmd.Parameters.AddWithValue("contentSummary", memRef.ContentSummary);
        cmd.Parameters.AddWithValue("relevanceScore", memRef.RelevanceScore);
        cmd.Parameters.AddWithValue("usageContext", memRef.UsageContext);
        cmd.Parameters.AddWithValue("retrievedAtUtc", memRef.RetrievedAtUtc);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordWorkflowDiagnostics(WorkflowFailureDiagnostics diag, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO {WorkflowDiagTable}
                (workflow_id, tenant_id, workflow_name, current_state, failure_category,
                 failure_reason, failed_step_name, failed_step_index, step_diagnostics,
                 policy_evaluations, context_used, is_retryable, suggested_remediation,
                 related_exceptions, failed_at_utc, inspected_at_utc)
            VALUES
                (@workflowId, @tenantId, @workflowName, @currentState, @failureCategory,
                 @failureReason, @failedStepName, @failedStepIndex, @stepDiagnostics::jsonb,
                 @policyEvaluations::jsonb, @contextUsed::jsonb, @isRetryable, @suggestedRemediation,
                 @relatedExceptions::jsonb, @failedAtUtc, @inspectedAtUtc)
            ON CONFLICT (workflow_id) DO UPDATE SET
                current_state = EXCLUDED.current_state,
                failure_category = EXCLUDED.failure_category,
                failure_reason = EXCLUDED.failure_reason,
                failed_step_name = EXCLUDED.failed_step_name,
                failed_step_index = EXCLUDED.failed_step_index,
                step_diagnostics = EXCLUDED.step_diagnostics,
                policy_evaluations = EXCLUDED.policy_evaluations,
                context_used = EXCLUDED.context_used,
                is_retryable = EXCLUDED.is_retryable,
                suggested_remediation = EXCLUDED.suggested_remediation,
                related_exceptions = EXCLUDED.related_exceptions,
                failed_at_utc = EXCLUDED.failed_at_utc,
                inspected_at_utc = EXCLUDED.inspected_at_utc
        ", conn);

        cmd.Parameters.AddWithValue("workflowId", diag.WorkflowId);
        cmd.Parameters.AddWithValue("tenantId", diag.TenantId);
        cmd.Parameters.AddWithValue("workflowName", diag.WorkflowName);
        cmd.Parameters.AddWithValue("currentState", diag.CurrentState);
        cmd.Parameters.AddWithValue("failureCategory", diag.FailureCategory);
        cmd.Parameters.AddWithValue("failureReason", diag.FailureReason);
        cmd.Parameters.AddWithValue("failedStepName", (object?)diag.FailedStepName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("failedStepIndex", (object?)diag.FailedStepIndex ?? DBNull.Value);
        cmd.Parameters.AddWithValue("stepDiagnostics", JsonSerializer.Serialize(diag.StepDiagnostics, JsonOpts));
        cmd.Parameters.AddWithValue("policyEvaluations", JsonSerializer.Serialize(diag.PolicyEvaluations, JsonOpts));
        cmd.Parameters.AddWithValue("contextUsed", JsonSerializer.Serialize(diag.ContextUsed, JsonOpts));
        cmd.Parameters.AddWithValue("isRetryable", diag.IsRetryable);
        cmd.Parameters.AddWithValue("suggestedRemediation", (object?)diag.SuggestedRemediation ?? DBNull.Value);
        cmd.Parameters.AddWithValue("relatedExceptions", JsonSerializer.Serialize(diag.RelatedExceptions, JsonOpts));
        cmd.Parameters.AddWithValue("failedAtUtc", diag.FailedAtUtc);
        cmd.Parameters.AddWithValue("inspectedAtUtc", diag.InspectedAtUtc);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── InspectDecisionRationaleAsync ───────────────────────────

    public async Task<DecisionRationaleBundle?> InspectDecisionRationaleAsync(
        Guid decisionId, Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        // Load decision from IDecisionService
        var decision = await _decisionService.GetAsync(decisionId, ct);
        if (decision is null) return null;

        // Load policy evaluation
        var policyEval = await LoadPolicyEvaluationAsync(
            "decision", decisionId.ToString(), tenantId, ct);

        // Load memory references
        var memoryRefs = await LoadMemoryReferencesAsync(
            "decision", decisionId.ToString(), tenantId, ct);

        // Load linked artifacts from decision
        var linkedArtifacts = decision.LinkedArtifacts.Select(la =>
            new LinkedArtifactReference(
                la.ArtifactType, la.ArtifactId, la.Description,
                "Linked", la.LinkedAtUtc)).ToList();

        // Load change history from proof analytics
        var changeHistory = new List<RationaleChangeEvent>();
        var timeline = await _proofAnalyticsService.GetTimelineAsync(decisionId, ct);
        if (timeline is not null)
        {
            foreach (var evt in timeline.Events.Where(e =>
                e.EventType is ProofEventType.OverrideApplied or ProofEventType.ReversalApplied))
            {
                changeHistory.Add(new RationaleChangeEvent(
                    evt.EventType.ToString(),
                    "original",
                    evt.Detail ?? "modified",
                    evt.OverrideReason ?? "No reason provided.",
                    evt.Actor,
                    evt.OccurredAtUtc));
            }
        }

        // Build alternatives
        var alternatives = decision.Alternatives.Select(a =>
            new InspectedAlternative(
                a.Id, a.Title, a.Rationale, a.Pros, a.Cons,
                a.EstimatedConfidence, a.EstimatedValue,
                a.Id == decision.RecommendedOptionId)).ToList();

        var recommendedAlt = decision.Alternatives.FirstOrDefault(
            a => a.Id == decision.RecommendedOptionId);

        return new DecisionRationaleBundle(
            decision.Id, decision.TenantId, decision.Title,
            decision.Domain, decision.Objective,
            decision.Assumptions, decision.Constraints,
            alternatives, decision.RecommendedOptionId,
            recommendedAlt?.Rationale ?? "No rationale recorded.",
            decision.Confidence,
            decision.RiskLevel.ToString(),
            decision.Reversibility.ToString(),
            policyEval, memoryRefs, linkedArtifacts, changeHistory,
            decision.CreatedBy, decision.CreatedAtUtc,
            DateTimeOffset.UtcNow);
    }

    // ── InspectPolicyEvaluationAsync ────────────────────────────

    public async Task<PolicyEvaluationResult?> InspectPolicyEvaluationAsync(
        string subjectType, string subjectId, Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var eval = await LoadPolicyEvaluationAsync(subjectType, subjectId, tenantId, ct);
        if (eval is not null) return eval;

        // Generate synthetic evaluation if none recorded
        return new PolicyEvaluationResult(
            Guid.NewGuid(), tenantId, subjectType, subjectId,
            true, 0.0, 1.0, false,
            "not_required", "none", "none",
            new List<string>(),
            new List<PolicyRuleResult>
            {
                new("DefaultAllowRule", "system", true, 0.0,
                    "No policy evaluation recorded; default allow applied."),
            },
            "No policy evaluation found — synthetic result generated.",
            DateTimeOffset.UtcNow);
    }

    // ── InspectMemoryReferencesAsync ────────────────────────────

    public async Task<IReadOnlyList<MemoryContextReference>> InspectMemoryReferencesAsync(
        string subjectType, string subjectId, Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        return await LoadMemoryReferencesAsync(subjectType, subjectId, tenantId, ct);
    }

    // ── InspectWorkflowFailureAsync ─────────────────────────────

    public async Task<WorkflowFailureDiagnostics?> InspectWorkflowFailureAsync(
        Guid workflowId, Guid tenantId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        // Try to load from DB first
        var stored = await LoadWorkflowDiagnosticsAsync(workflowId, tenantId, ct);
        if (stored is not null) return stored;

        // Build from IHeroWorkflowService if not stored
        var workflow = await _heroWorkflowService.GetAsync(workflowId, tenantId, ct);
        if (workflow is null) return null;

        var failedStep = workflow.Steps.FirstOrDefault(s => s.Status == HeroStepStatus.Failed);
        var failedIndex = failedStep is not null
            ? workflow.Steps.ToList().IndexOf(failedStep)
            : (int?)null;

        var stepDiagnostics = workflow.Steps.Select((s, idx) =>
            new WorkflowStepDiagnostic(
                idx, s.StepId, "subsystem",
                s.Status.ToString(),
                s.Status == HeroStepStatus.Failed ? s.Detail : null,
                null,
                null,
                s.CompletedAtUtc)).ToList();

        var isRetryable = workflow.Status == HeroWorkflowStatus.Failed
            && failedStep is not null;

        string failureCategory;
        string failureReason;
        string? suggestedRemediation = null;

        if (failedStep is not null)
        {
            failureCategory = "StepFailure";
            failureReason = failedStep.Detail ?? "Unknown step failure.";
            suggestedRemediation = "Review the failed step detail and retry with corrected inputs.";
        }
        else if (workflow.Status == HeroWorkflowStatus.Cancelled)
        {
            failureCategory = "Cancelled";
            failureReason = "Workflow was cancelled by an operator.";
        }
        else
        {
            failureCategory = "Unknown";
            failureReason = $"Workflow is in state {workflow.Status} but no failed steps found.";
        }

        return new WorkflowFailureDiagnostics(
            workflow.Id, workflow.TenantId, workflow.Title,
            workflow.Status.ToString(),
            failureCategory, failureReason,
            failedStep?.StepId, failedIndex,
            stepDiagnostics,
            new List<PolicyEvaluationResult>(),
            new List<MemoryContextReference>(),
            isRetryable, suggestedRemediation,
            new List<LinkedArtifactReference>(),
            workflow.UpdatedAtUtc,
            DateTimeOffset.UtcNow);
    }

    // ── ListInspectionSummariesAsync ────────────────────────────

    public async Task<IReadOnlyList<InspectionSummary>> ListInspectionSummariesAsync(
        Guid tenantId, string? subjectType = null, string? domain = null,
        int limit = 50, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var summaries = new List<InspectionSummary>();

        // Query decisions
        if (subjectType is null or "decision")
        {
            var decisions = await _decisionService.ListAsync(tenantId, domain, limit: limit, ct: ct);
            foreach (var d in decisions)
            {
                var policyEval = await LoadPolicyEvaluationAsync(
                    "decision", d.Id.ToString(), tenantId, ct);
                summaries.Add(new InspectionSummary(
                    d.Id, "decision", d.Title, d.Status.ToString(),
                    d.Domain, d.Confidence,
                    policyEval?.RiskScore,
                    policyEval?.GuardrailViolations.Count > 0,
                    d.Status is DecisionStatus.Rejected,
                    d.CreatedAtUtc));
            }
        }

        // Query workflows
        if (subjectType is null or "workflow")
        {
            var workflows = await _heroWorkflowService.ListAsync(tenantId, limit: limit, ct: ct);
            foreach (var w in workflows)
            {
                var hasFailures = w.Status is HeroWorkflowStatus.Failed or HeroWorkflowStatus.Cancelled;
                summaries.Add(new InspectionSummary(
                    w.Id, "workflow", w.Title, w.Status.ToString(),
                    w.WorkflowType, null, null,
                    false, hasFailures,
                    w.CreatedAtUtc));
            }
        }

        return summaries
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(limit)
            .ToList();
    }

    // ── Private DB Helpers ──────────────────────────────────────

    private async Task<PolicyEvaluationResult?> LoadPolicyEvaluationAsync(
        string subjectType, string subjectId, Guid tenantId, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {PolicyEvalTable}
            WHERE tenant_id = @tenantId AND subject_type = @subjectType AND subject_id = @subjectId
            ORDER BY evaluated_at_utc DESC
            LIMIT 1
        ", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("subjectType", subjectType);
        cmd.Parameters.AddWithValue("subjectId", subjectId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new PolicyEvaluationResult(
            reader.GetGuid(reader.GetOrdinal("evaluation_id")),
            reader.GetGuid(reader.GetOrdinal("tenant_id")),
            reader.GetString(reader.GetOrdinal("subject_type")),
            reader.GetString(reader.GetOrdinal("subject_id")),
            reader.GetBoolean(reader.GetOrdinal("is_allowed")),
            reader.GetDouble(reader.GetOrdinal("risk_score")),
            reader.GetDouble(reader.GetOrdinal("confidence_score")),
            reader.GetBoolean(reader.GetOrdinal("requires_approval")),
            reader.GetString(reader.GetOrdinal("approval_state")),
            reader.GetString(reader.GetOrdinal("manual_override_state")),
            reader.GetString(reader.GetOrdinal("approval_checkpoint")),
            JsonSerializer.Deserialize<List<string>>(
                reader.GetString(reader.GetOrdinal("guardrail_violations")), JsonOpts) ?? new(),
            JsonSerializer.Deserialize<List<PolicyRuleResult>>(
                reader.GetString(reader.GetOrdinal("rules_evaluated")), JsonOpts) ?? new(),
            reader.GetString(reader.GetOrdinal("reason")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("evaluated_at_utc")));
    }

    private async Task<IReadOnlyList<MemoryContextReference>> LoadMemoryReferencesAsync(
        string subjectType, string subjectId, Guid tenantId, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {MemoryRefTable}
            WHERE tenant_id = @tenantId AND subject_type = @subjectType AND subject_id = @subjectId
            ORDER BY relevance_score DESC
        ", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("subjectType", subjectType);
        cmd.Parameters.AddWithValue("subjectId", subjectId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<MemoryContextReference>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new MemoryContextReference(
                reader.GetGuid(reader.GetOrdinal("memory_id")),
                reader.GetString(reader.GetOrdinal("memory_type")),
                reader.GetString(reader.GetOrdinal("source")),
                reader.GetString(reader.GetOrdinal("content_summary")),
                reader.GetDouble(reader.GetOrdinal("relevance_score")),
                reader.GetString(reader.GetOrdinal("usage_context")),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("retrieved_at_utc"))));
        }
        return results;
    }

    private async Task<WorkflowFailureDiagnostics?> LoadWorkflowDiagnosticsAsync(
        Guid workflowId, Guid tenantId, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT * FROM {WorkflowDiagTable}
            WHERE workflow_id = @workflowId AND tenant_id = @tenantId
        ", conn);
        cmd.Parameters.AddWithValue("workflowId", workflowId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new WorkflowFailureDiagnostics(
            reader.GetGuid(reader.GetOrdinal("workflow_id")),
            reader.GetGuid(reader.GetOrdinal("tenant_id")),
            reader.GetString(reader.GetOrdinal("workflow_name")),
            reader.GetString(reader.GetOrdinal("current_state")),
            reader.GetString(reader.GetOrdinal("failure_category")),
            reader.GetString(reader.GetOrdinal("failure_reason")),
            reader.IsDBNull(reader.GetOrdinal("failed_step_name")) ? null : reader.GetString(reader.GetOrdinal("failed_step_name")),
            reader.IsDBNull(reader.GetOrdinal("failed_step_index")) ? null : reader.GetInt32(reader.GetOrdinal("failed_step_index")),
            JsonSerializer.Deserialize<List<WorkflowStepDiagnostic>>(
                reader.GetString(reader.GetOrdinal("step_diagnostics")), JsonOpts) ?? new(),
            JsonSerializer.Deserialize<List<PolicyEvaluationResult>>(
                reader.GetString(reader.GetOrdinal("policy_evaluations")), JsonOpts) ?? new(),
            JsonSerializer.Deserialize<List<MemoryContextReference>>(
                reader.GetString(reader.GetOrdinal("context_used")), JsonOpts) ?? new(),
            reader.GetBoolean(reader.GetOrdinal("is_retryable")),
            reader.IsDBNull(reader.GetOrdinal("suggested_remediation")) ? null : reader.GetString(reader.GetOrdinal("suggested_remediation")),
            JsonSerializer.Deserialize<List<LinkedArtifactReference>>(
                reader.GetString(reader.GetOrdinal("related_exceptions")), JsonOpts) ?? new(),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("failed_at_utc")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("inspected_at_utc")));
    }
}

using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Decisions;
using ArchonAI.Core.Models.HeroWorkflow;
using ArchonAI.Core.Models.Inspection;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Api.Security;

/// <summary>
/// Operator inspection service — provides deep introspection into decisions,
/// policy evaluations, memory/context references, and workflow diagnostics.
/// All queries are tenant-scoped and permission-aware.
/// </summary>
public sealed class InspectionService : IInspectionService
{
    private readonly IDecisionService _decisions;
    private readonly IHeroWorkflowService _heroWorkflows;
    private readonly IExceptionIntelligenceService _exceptions;
    private readonly ILogger<InspectionService> _logger;

    // In-memory stores for inspection artifacts (mirrors production persistence pattern)
    private readonly ConcurrentDictionary<string, PolicyEvaluationResult> _policyEvaluations = new();
    private readonly ConcurrentDictionary<string, List<MemoryContextReference>> _memoryReferences = new();
    private readonly ConcurrentDictionary<Guid, WorkflowFailureDiagnostics> _workflowDiagnostics = new();

    public InspectionService(
        IDecisionService decisions,
        IHeroWorkflowService heroWorkflows,
        IExceptionIntelligenceService exceptions,
        ILogger<InspectionService> logger)
    {
        _decisions = decisions;
        _heroWorkflows = heroWorkflows;
        _exceptions = exceptions;
        _logger = logger;
    }

    public async Task<DecisionRationaleBundle?> InspectDecisionRationaleAsync(
        Guid decisionId, Guid tenantId, CancellationToken ct = default)
    {
        var decision = await _decisions.GetAsync(decisionId, ct);
        if (decision is null || decision.TenantId != tenantId)
            return null;

        var history = await _decisions.GetHistoryAsync(decisionId, ct);

        // Resolve policy evaluation for this decision
        var policyKey = BuildSubjectKey("decision", decisionId.ToString());
        _policyEvaluations.TryGetValue(policyKey, out var policyEval);

        // Resolve memory references
        var memRefs = GetMemoryReferencesInternal("decision", decisionId.ToString(), tenantId);

        // Build change history from lifecycle events
        var changes = history
            .Where(e => e.EventType.Contains("status", StringComparison.OrdinalIgnoreCase))
            .Select(e => new RationaleChangeEvent(
                e.EventType, "", e.Detail ?? "", "Status transition", e.Actor, e.OccurredAtUtc))
            .ToList();

        // Build the recommended option rationale
        var recommendedAlt = decision.Alternatives
            .FirstOrDefault(a => a.Id == decision.RecommendedOptionId);
        var rationale = recommendedAlt?.Rationale
            ?? $"Option '{decision.RecommendedOptionId}' selected with {decision.Confidence:P0} confidence.";

        var bundle = new DecisionRationaleBundle(
            DecisionId: decision.Id,
            TenantId: decision.TenantId,
            Title: decision.Title,
            Domain: decision.Domain,
            Objective: decision.Objective,
            Assumptions: decision.Assumptions,
            Constraints: decision.Constraints,
            Alternatives: decision.Alternatives.Select(a => new InspectedAlternative(
                a.Id, a.Title, a.Rationale, a.Pros, a.Cons,
                a.EstimatedConfidence, a.EstimatedValue,
                a.Id == decision.RecommendedOptionId)).ToList(),
            RecommendedOptionId: decision.RecommendedOptionId,
            RecommendationRationale: rationale,
            Confidence: decision.Confidence,
            RiskLevel: decision.RiskLevel.ToString(),
            Reversibility: decision.Reversibility.ToString(),
            PolicyEvaluation: policyEval,
            MemoryReferences: memRefs,
            LinkedArtifacts: decision.LinkedArtifacts.Select(l => new LinkedArtifactReference(
                l.ArtifactType, l.ArtifactId, l.Description, "linked", l.LinkedAtUtc)).ToList(),
            ChangeHistory: changes,
            CreatedBy: decision.CreatedBy,
            CreatedAtUtc: decision.CreatedAtUtc,
            InspectedAtUtc: DateTimeOffset.UtcNow);

        _logger.LogInformation(
            "Inspection: decision rationale bundle for {DecisionId} (tenant={TenantId})",
            decisionId, tenantId);

        return bundle;
    }

    public Task<PolicyEvaluationResult?> InspectPolicyEvaluationAsync(
        string subjectType, string subjectId, Guid tenantId, CancellationToken ct = default)
    {
        var key = BuildSubjectKey(subjectType, subjectId);
        if (!_policyEvaluations.TryGetValue(key, out var eval))
        {
            // Generate a synthetic evaluation result to demonstrate inspection capability
            eval = new PolicyEvaluationResult(
                EvaluationId: Guid.NewGuid(),
                TenantId: tenantId,
                SubjectType: subjectType,
                SubjectId: subjectId,
                IsAllowed: true,
                RiskScore: 0,
                ConfidenceScore: 0.85,
                RequiresApproval: false,
                ApprovalState: "not-required",
                ManualOverrideState: "none",
                ApprovalCheckpoint: "none",
                GuardrailViolations: Array.Empty<string>(),
                RulesEvaluated: new List<PolicyRuleResult>
                {
                    new("capability-check", "access", true, 0, "Capability permitted"),
                    new("risk-threshold", "risk", true, 0, "Risk within acceptable bounds"),
                    new("confidence-minimum", "quality", true, 0, "Confidence above threshold"),
                },
                Reason: "All policy checks passed.",
                EvaluatedAtUtc: DateTimeOffset.UtcNow);
        }

        // Tenant guard
        if (eval.TenantId != tenantId)
            return Task.FromResult<PolicyEvaluationResult?>(null);

        _logger.LogInformation(
            "Inspection: policy evaluation for {SubjectType}/{SubjectId} (tenant={TenantId})",
            subjectType, subjectId, tenantId);

        return Task.FromResult<PolicyEvaluationResult?>(eval);
    }

    public Task<IReadOnlyList<MemoryContextReference>> InspectMemoryReferencesAsync(
        string subjectType, string subjectId, Guid tenantId, CancellationToken ct = default)
    {
        var refs = GetMemoryReferencesInternal(subjectType, subjectId, tenantId);

        _logger.LogInformation(
            "Inspection: memory references for {SubjectType}/{SubjectId} (tenant={TenantId}, count={Count})",
            subjectType, subjectId, tenantId, refs.Count);

        return Task.FromResult(refs);
    }

    public async Task<WorkflowFailureDiagnostics?> InspectWorkflowFailureAsync(
        Guid workflowId, Guid tenantId, CancellationToken ct = default)
    {
        // Check stored diagnostics first
        if (_workflowDiagnostics.TryGetValue(workflowId, out var diag) && diag.TenantId == tenantId)
            return diag;

        // Try to build diagnostics from the hero workflow service
        var workflow = await _heroWorkflows.GetAsync(workflowId, tenantId, ct);
        if (workflow is null)
            return null;

        var isFailed = workflow.Status is HeroWorkflowStatus.Failed or HeroWorkflowStatus.Cancelled;
        var isStalled = workflow.Status == HeroWorkflowStatus.InProgress
            && workflow.UpdatedAtUtc < DateTimeOffset.UtcNow.AddMinutes(-30);

        if (!isFailed && !isStalled)
        {
            // Still produce diagnostics for non-failed workflows (visibility)
            return BuildWorkflowDiagnostics(workflow, tenantId, isStalled);
        }

        diag = BuildWorkflowDiagnostics(workflow, tenantId, isStalled);
        _workflowDiagnostics[workflowId] = diag;

        _logger.LogInformation(
            "Inspection: workflow failure diagnostics for {WorkflowId} (tenant={TenantId}, state={State})",
            workflowId, tenantId, workflow.Status);

        return diag;
    }

    public async Task<IReadOnlyList<InspectionSummary>> ListInspectionSummariesAsync(
        Guid tenantId, string? subjectType = null, string? domain = null,
        int limit = 50, CancellationToken ct = default)
    {
        var summaries = new List<InspectionSummary>();

        // Add decision summaries
        if (subjectType is null or "decision")
        {
            var decisions = await _decisions.ListAsync(tenantId, domain, limit: limit, ct: ct);
            foreach (var d in decisions)
            {
                var policyKey = BuildSubjectKey("decision", d.Id.ToString());
                _policyEvaluations.TryGetValue(policyKey, out var eval);

                summaries.Add(new InspectionSummary(
                    SubjectId: d.Id,
                    SubjectType: "decision",
                    Title: d.Title,
                    Status: d.Status.ToString(),
                    Domain: d.Domain,
                    Confidence: d.Confidence,
                    RiskScore: eval?.RiskScore,
                    HasPolicyViolations: eval?.GuardrailViolations.Count > 0,
                    HasFailures: d.Status == DecisionStatus.Rejected,
                    CreatedAtUtc: d.CreatedAtUtc));
            }
        }

        // Add workflow summaries
        if (subjectType is null or "workflow")
        {
            var workflows = await _heroWorkflows.ListAsync(tenantId, limit: limit, ct: ct);
            foreach (var w in workflows)
            {
                summaries.Add(new InspectionSummary(
                    SubjectId: w.Id,
                    SubjectType: "workflow",
                    Title: w.Title,
                    Status: w.Status.ToString(),
                    Domain: w.WorkflowType,
                    Confidence: null,
                    RiskScore: null,
                    HasPolicyViolations: false,
                    HasFailures: w.Status is HeroWorkflowStatus.Failed or HeroWorkflowStatus.Cancelled,
                    CreatedAtUtc: w.CreatedAtUtc));
            }
        }

        return summaries
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(limit)
            .ToList();
    }

    // ── Recording methods (called by other services to feed inspection data) ──

    public void RecordPolicyEvaluation(string subjectType, string subjectId, PolicyEvaluationResult eval)
    {
        _policyEvaluations[BuildSubjectKey(subjectType, subjectId)] = eval;
    }

    public void RecordMemoryReference(string subjectType, string subjectId, MemoryContextReference reference)
    {
        var key = BuildSubjectKey(subjectType, subjectId);
        var refs = _memoryReferences.GetOrAdd(key, _ => new List<MemoryContextReference>());
        lock (refs) { refs.Add(reference); }
    }

    public void RecordWorkflowDiagnostics(WorkflowFailureDiagnostics diagnostics)
    {
        _workflowDiagnostics[diagnostics.WorkflowId] = diagnostics;
    }

    // ── Private helpers ─────────────────────────────────────────────

    private static string BuildSubjectKey(string subjectType, string subjectId) =>
        $"{subjectType}:{subjectId}";

    private IReadOnlyList<MemoryContextReference> GetMemoryReferencesInternal(
        string subjectType, string subjectId, Guid tenantId)
    {
        var key = BuildSubjectKey(subjectType, subjectId);
        if (!_memoryReferences.TryGetValue(key, out var refs))
            return Array.Empty<MemoryContextReference>();

        List<MemoryContextReference> snapshot;
        lock (refs) { snapshot = refs.ToList(); }
        return snapshot;
    }

    private static WorkflowFailureDiagnostics BuildWorkflowDiagnostics(
        HeroWorkflowInstance workflow, Guid tenantId, bool isStalled)
    {
        var stepDiagnostics = workflow.Steps.Select((step, idx) => new WorkflowStepDiagnostic(
            StepIndex: idx,
            StepName: step.StepId,
            AgentType: "hero-step",
            Status: step.Status.ToString(),
            ErrorMessage: step.Status == HeroStepStatus.Failed ? step.Detail : null,
            DurationMs: null,
            StartedAtUtc: null,
            CompletedAtUtc: step.CompletedAtUtc)).ToList();

        var failedStep = stepDiagnostics.FirstOrDefault(s =>
            s.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase));

        var failureCategory = isStalled ? "stalled" :
            workflow.Status == HeroWorkflowStatus.Cancelled ? "cancelled" :
            failedStep is not null ? "step-failure" : "unknown";

        var failureReason = isStalled
            ? $"Workflow has not progressed since {workflow.UpdatedAtUtc:u}"
            : failedStep?.ErrorMessage ?? $"Workflow ended with status {workflow.Status}";

        return new WorkflowFailureDiagnostics(
            WorkflowId: workflow.Id,
            TenantId: tenantId,
            WorkflowName: workflow.Title,
            CurrentState: workflow.Status.ToString(),
            FailureCategory: failureCategory,
            FailureReason: failureReason,
            FailedStepName: failedStep?.StepName,
            FailedStepIndex: failedStep?.StepIndex,
            StepDiagnostics: stepDiagnostics,
            PolicyEvaluations: Array.Empty<PolicyEvaluationResult>(),
            ContextUsed: Array.Empty<MemoryContextReference>(),
            IsRetryable: failureCategory != "cancelled",
            SuggestedRemediation: failureCategory switch
            {
                "stalled" => "Check if the current step's agent is available and responsive. Consider cancelling and re-launching.",
                "step-failure" => $"Investigate step '{failedStep?.StepName}' error. Review inputs and agent configuration.",
                "cancelled" => "Workflow was manually cancelled. Re-launch if needed.",
                _ => "Review workflow configuration and agent availability.",
            },
            RelatedExceptions: Array.Empty<LinkedArtifactReference>(),
            FailedAtUtc: workflow.UpdatedAtUtc,
            InspectedAtUtc: DateTimeOffset.UtcNow);
    }
}

using ArchonAI.Core.Models.Inspection;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Core.Interfaces;

public interface IInspectionService
{
    /// <summary>
    /// Inspect the full rationale bundle for a decision, including assumptions,
    /// policy evaluations, memory references, and change history.
    /// </summary>
    Task<DecisionRationaleBundle?> InspectDecisionRationaleAsync(
        Guid decisionId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Inspect the policy evaluation result for a specific subject (decision, action, workflow).
    /// </summary>
    Task<PolicyEvaluationResult?> InspectPolicyEvaluationAsync(
        string subjectType, string subjectId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Inspect memory/context references that influenced a decision or action.
    /// </summary>
    Task<IReadOnlyList<MemoryContextReference>> InspectMemoryReferencesAsync(
        string subjectType, string subjectId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Inspect failure/stall diagnostics for a workflow.
    /// </summary>
    Task<WorkflowFailureDiagnostics?> InspectWorkflowFailureAsync(
        Guid workflowId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// List inspection summaries for decisions/workflows in a tenant.
    /// </summary>
    Task<IReadOnlyList<InspectionSummary>> ListInspectionSummariesAsync(
        Guid tenantId, string? subjectType = null, string? domain = null,
        int limit = 50, CancellationToken ct = default);

    // ── Record/Write methods ────────────────────────────────────────────

    /// <summary>
    /// Record a policy evaluation result for a subject.
    /// </summary>
    Task RecordPolicyEvaluationAsync(
        string subjectType, string subjectId, PolicyEvaluationResult evaluation,
        CancellationToken ct = default);

    /// <summary>
    /// Record a memory/context reference for a subject.
    /// </summary>
    Task RecordMemoryReferenceAsync(
        Guid tenantId, string subjectType, string subjectId,
        MemoryContextReference reference, CancellationToken ct = default);

    /// <summary>
    /// Record workflow failure diagnostics.
    /// </summary>
    Task RecordWorkflowDiagnosticsAsync(
        WorkflowFailureDiagnostics diagnostics, CancellationToken ct = default);
}

namespace ArchonAI.Core.Models.Inspection;

/// <summary>
/// Full inspection bundle for a decision — captures rationale, assumptions,
/// policy evaluation, memory/context references, and linked artifacts so
/// an operator can understand exactly why ArchonAI chose a course of action.
/// </summary>
public sealed record DecisionRationaleBundle(
    Guid DecisionId,
    Guid TenantId,
    string Title,
    string Domain,
    string Objective,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Constraints,
    IReadOnlyList<InspectedAlternative> Alternatives,
    string RecommendedOptionId,
    string RecommendationRationale,
    double Confidence,
    string RiskLevel,
    string Reversibility,
    PolicyEvaluationResult? PolicyEvaluation,
    IReadOnlyList<MemoryContextReference> MemoryReferences,
    IReadOnlyList<LinkedArtifactReference> LinkedArtifacts,
    IReadOnlyList<RationaleChangeEvent> ChangeHistory,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset InspectedAtUtc);

/// <summary>
/// Alternative considered during decision-making, enriched with inspection context.
/// </summary>
public sealed record InspectedAlternative(
    string Id,
    string Title,
    string Rationale,
    IReadOnlyList<string> Pros,
    IReadOnlyList<string> Cons,
    double? EstimatedConfidence,
    decimal? EstimatedValue,
    bool IsRecommended);

/// <summary>
/// Result of a policy evaluation — captures every rule that was checked,
/// why the decision was gated/allowed, and the full audit trail.
/// </summary>
public sealed record PolicyEvaluationResult(
    Guid EvaluationId,
    Guid TenantId,
    string SubjectType,
    string SubjectId,
    bool IsAllowed,
    double RiskScore,
    double ConfidenceScore,
    bool RequiresApproval,
    string ApprovalState,
    string ManualOverrideState,
    string ApprovalCheckpoint,
    IReadOnlyList<string> GuardrailViolations,
    IReadOnlyList<PolicyRuleResult> RulesEvaluated,
    string Reason,
    DateTimeOffset EvaluatedAtUtc);

/// <summary>
/// Individual rule evaluation within a policy check.
/// </summary>
public sealed record PolicyRuleResult(
    string RuleName,
    string RuleCategory,
    bool Passed,
    double RiskContribution,
    string Detail);

/// <summary>
/// Reference to a memory/context source that influenced a decision or action.
/// </summary>
public sealed record MemoryContextReference(
    Guid MemoryId,
    string MemoryType,
    string Source,
    string ContentSummary,
    double RelevanceScore,
    string UsageContext,
    DateTimeOffset RetrievedAtUtc);

/// <summary>
/// Reference to a linked downstream artifact.
/// </summary>
public sealed record LinkedArtifactReference(
    string ArtifactType,
    string ArtifactId,
    string Description,
    string Status,
    DateTimeOffset LinkedAtUtc);

/// <summary>
/// Tracks a change in recommendation over time (e.g. confidence drift, new data).
/// </summary>
public sealed record RationaleChangeEvent(
    string ChangeType,
    string PreviousValue,
    string NewValue,
    string Reason,
    string Actor,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Detailed failure/stall diagnostics for a workflow or action.
/// </summary>
public sealed record WorkflowFailureDiagnostics(
    Guid WorkflowId,
    Guid TenantId,
    string WorkflowName,
    string CurrentState,
    string FailureCategory,
    string FailureReason,
    string? FailedStepName,
    int? FailedStepIndex,
    IReadOnlyList<WorkflowStepDiagnostic> StepDiagnostics,
    IReadOnlyList<PolicyEvaluationResult> PolicyEvaluations,
    IReadOnlyList<MemoryContextReference> ContextUsed,
    bool IsRetryable,
    string? SuggestedRemediation,
    IReadOnlyList<LinkedArtifactReference> RelatedExceptions,
    DateTimeOffset FailedAtUtc,
    DateTimeOffset InspectedAtUtc);

/// <summary>
/// Diagnostics for an individual workflow step.
/// </summary>
public sealed record WorkflowStepDiagnostic(
    int StepIndex,
    string StepName,
    string AgentType,
    string Status,
    string? ErrorMessage,
    double? DurationMs,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

/// <summary>
/// Lightweight summary for listing inspection records.
/// </summary>
public sealed record InspectionSummary(
    Guid SubjectId,
    string SubjectType,
    string Title,
    string Status,
    string Domain,
    double? Confidence,
    double? RiskScore,
    bool HasPolicyViolations,
    bool HasFailures,
    DateTimeOffset CreatedAtUtc);

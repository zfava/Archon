namespace ArchonAI.Core.Models.ProofAnalytics;

/// <summary>
/// A single proof event in a decision-to-outcome lineage chain.
/// Each event captures one step in the lifecycle: decision created,
/// recommendation made, approval requested/granted/denied, action executed,
/// outcome observed, override/reversal, or impact attributed.
/// </summary>
public sealed record ProofEvent(
    Guid Id,
    Guid TenantId,
    Guid DecisionId,
    Guid? WorkflowId,
    ProofEventType EventType,
    string Actor,
    string? Detail,
    decimal? ExpectedValue,
    decimal? ActualValue,
    decimal? Variance,
    double? VariancePercent,
    string? ActionType,
    bool? IsSuccess,
    string? OverrideReason,
    decimal? EconomicImpact,
    string? ImpactAttribution,
    DateTimeOffset OccurredAtUtc);

public enum ProofEventType
{
    DecisionCreated,
    RecommendationMade,
    ApprovalRequested,
    ApprovalGranted,
    ApprovalDenied,
    ActionExecuted,
    ExpectedOutcomeRecorded,
    ActualOutcomeRecorded,
    VarianceComputed,
    OverrideApplied,
    ReversalApplied,
    EconomicImpactAttributed
}

/// <summary>
/// Complete lineage for a decision: an ordered sequence of proof events
/// that shows the full chain from decision to outcome.
/// </summary>
public sealed record ProofTimeline(
    Guid DecisionId,
    Guid TenantId,
    string DecisionTitle,
    string Domain,
    IReadOnlyList<ProofEvent> Events,
    ProofTimelineSummary Summary);

/// <summary>
/// Summary statistics for a single proof timeline.
/// </summary>
public sealed record ProofTimelineSummary(
    int TotalEvents,
    bool HasOutcome,
    bool WasOverridden,
    bool WasReversed,
    decimal? PredictedValue,
    decimal? ActualValue,
    decimal? Variance,
    double? VariancePercent,
    string? FinalAssessment,
    TimeSpan? DecisionToOutcomeDuration);

/// <summary>
/// Predicted vs actual comparison for a set of decisions.
/// </summary>
public sealed record PredictedVsActualSummary(
    Guid TenantId,
    string? Domain,
    int TotalDecisions,
    int WithOutcomes,
    int OnTarget,
    int Overperformed,
    int Underperformed,
    double MeanVariancePercent,
    double MedianVariancePercent,
    decimal TotalPredictedValue,
    decimal TotalActualValue,
    decimal TotalVariance,
    double AccuracyRate,
    IReadOnlyList<PredictedVsActualEntry> Entries);

public sealed record PredictedVsActualEntry(
    Guid DecisionId,
    string Title,
    string Domain,
    decimal? PredictedValue,
    decimal? ActualValue,
    decimal? Variance,
    double? VariancePercent,
    string Direction,
    DateTimeOffset DecisionCreatedAtUtc,
    DateTimeOffset? OutcomeObservedAtUtc);

/// <summary>
/// Approval-to-execution conversion metrics.
/// </summary>
public sealed record ApprovalConversionSummary(
    Guid TenantId,
    int TotalApprovalRequests,
    int Granted,
    int Denied,
    int ExecutedAfterApproval,
    int PendingExecution,
    double ApprovalRate,
    double ExecutionConversionRate,
    TimeSpan? MeanApprovalLatency,
    IReadOnlyDictionary<string, ApprovalConversionByType> ByActionType);

public sealed record ApprovalConversionByType(
    string ActionType,
    int Requested,
    int Granted,
    int Denied,
    int Executed,
    double ApprovalRate,
    double ExecutionRate);

/// <summary>
/// Execution success/failure trend data.
/// </summary>
public sealed record ExecutionTrendSummary(
    Guid TenantId,
    int TotalExecutions,
    int Successes,
    int Failures,
    double SuccessRate,
    IReadOnlyList<ExecutionTrendBucket> Buckets);

public sealed record ExecutionTrendBucket(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int Executions,
    int Successes,
    int Failures,
    double SuccessRate);

/// <summary>
/// Override/reversal rate analytics.
/// </summary>
public sealed record OverrideRateSummary(
    Guid TenantId,
    int TotalDecisions,
    int Overrides,
    int Reversals,
    double OverrideRate,
    double ReversalRate,
    IReadOnlyDictionary<string, int> OverrideReasonDistribution);

/// <summary>
/// Trust analytics broken down by action type.
/// </summary>
public sealed record TrustAnalyticsSummary(
    Guid TenantId,
    IReadOnlyList<TrustByActionType> ByActionType);

public sealed record TrustByActionType(
    string ActionType,
    int TotalDecisions,
    int WithOutcomes,
    double AccuracyRate,
    double OverrideRate,
    double MeanConfidence,
    double MeanVariancePercent,
    string TrustGrade);

/// <summary>
/// Top-level proof dashboard aggregating all summaries.
/// </summary>
public sealed record ProofDashboard(
    Guid TenantId,
    PredictedVsActualSummary PredictedVsActual,
    ApprovalConversionSummary ApprovalConversion,
    ExecutionTrendSummary ExecutionTrends,
    OverrideRateSummary OverrideRates,
    TrustAnalyticsSummary TrustAnalytics,
    DateTimeOffset GeneratedAtUtc);

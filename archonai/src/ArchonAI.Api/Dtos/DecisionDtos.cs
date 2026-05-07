namespace ArchonAI.Api.Dtos;

public sealed record CreateDecisionRequest(
    string Title,
    string Domain,
    string? Objective,
    IReadOnlyList<string>? Constraints,
    IReadOnlyList<string>? Assumptions,
    IReadOnlyList<CreateAlternativeRequest>? Alternatives,
    string? RecommendedOptionId,
    double? Confidence,
    string? Reversibility,
    string? RiskLevel,
    decimal? ExpectedValue,
    bool? RequiresApproval);

public sealed record CreateAlternativeRequest(
    string Title,
    string Rationale,
    IReadOnlyList<string>? Pros,
    IReadOnlyList<string>? Cons,
    double? EstimatedConfidence,
    decimal? EstimatedValue);

public sealed record UpdateDecisionStatusRequest(
    string Status,
    string? Detail);

public sealed record CreateDecisionLinkRequest(
    string ArtifactType,
    string ArtifactId,
    string? Description);

public sealed record AttachFinancialConsequenceRequest(
    decimal? ExpectedRevenueImpactLow,
    decimal? ExpectedRevenueImpactHigh,
    decimal? ExpectedCostImpactLow,
    decimal? ExpectedCostImpactHigh,
    decimal? ExpectedMarginImpact,
    string? ExpectedCashTimingImpact,
    string? LaborImpact,
    decimal? DownsideRisk,
    decimal? UpsidePotential,
    double? ConfidenceAdjustment,
    decimal? RoiEstimateLow,
    decimal? RoiEstimateHigh,
    string? BreakEvenEstimate,
    IReadOnlyList<string>? Assumptions,
    string? Notes);

public sealed record RecordExpectedOutcomeRequest(
    Guid DecisionId,
    string? ExpectedSummary,
    decimal? ExpectedValue,
    double ConfidenceAtPrediction,
    string? ExpectedTimeframe);

public sealed record RecordActualOutcomeRequest(
    Guid DecisionId,
    string? ActualSummary,
    decimal? ActualValue,
    string? RootCause,
    string? Notes);

public sealed record RecordProofEventRequest(
    Guid DecisionId,
    Guid? WorkflowId,
    string EventType,
    string? Detail,
    decimal? ExpectedValue,
    decimal? ActualValue,
    decimal? Variance,
    double? VariancePercent,
    string? ActionType,
    bool? IsSuccess,
    string? OverrideReason,
    decimal? EconomicImpact,
    string? ImpactAttribution);

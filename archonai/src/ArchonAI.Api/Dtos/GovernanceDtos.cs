namespace ArchonAI.Api.Dtos;

public sealed record CreateApprovalPolicyRequest(
    string ActionType,
    string Description,
    string RequiredApproverRole,
    bool RequireSeparationOfDuties);

public sealed record RequestApprovalRequest(
    string ActionType,
    string ResourceId,
    string Justification);

public sealed record ReviewApprovalRequest(
    bool Approve,
    string? Notes);

public sealed record SetTrustTierPolicyRequest(
    Guid? Id,
    string ActionScope,
    string MaxTier,
    double? ConfidenceThreshold,
    decimal? ValueCeiling,
    bool? RequireReversible,
    string? Description,
    bool? IsEnabled);

public sealed record EvaluateTrustTierRequest(
    string ActionScope,
    string RequestedTier,
    double? Confidence,
    decimal? Value,
    bool? Reversible);

public sealed record SetSafetyClassificationRequest(
    string ActionType,
    string Reversibility,
    bool RollbackSupported,
    string RollbackStrategy,
    int? RollbackWindowMinutes,
    string? CompensationDescription,
    string? OperatorNotes);

public sealed record RecordGovernedActionRequest(
    string ActionType,
    string Description,
    Guid? DecisionId,
    Guid? WorkflowId,
    Guid? ApprovalGateId);

public sealed record RunSimulationRequest(
    string ActionType,
    string? ActionScope,
    string Title,
    string? Domain,
    string? Objective,
    string? RiskLevel,
    string? Reversibility,
    double? Confidence,
    decimal? ExpectedValue,
    decimal? RevenueImpactLow,
    decimal? RevenueImpactHigh,
    decimal? CostImpactLow,
    decimal? CostImpactHigh,
    decimal? DownsideRisk,
    decimal? UpsidePotential,
    string? RequestedTier,
    string? WorkflowType);

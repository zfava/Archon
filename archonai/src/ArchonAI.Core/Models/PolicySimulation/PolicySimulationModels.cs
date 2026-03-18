using ArchonAI.Core.Models.Decisions;
using ArchonAI.Core.Models.Governance;

namespace ArchonAI.Core.Models.PolicySimulation;

/// <summary>
/// The input describing what action to simulate against the governance stack.
/// </summary>
public sealed record SimulationRequest(
    Guid TenantId,
    string ActionType,
    string ActionScope,
    string Title,
    string? Domain,
    string? Objective,
    // Decision parameters
    string? RiskLevel,
    string? Reversibility,
    double? Confidence,
    decimal? ExpectedValue,
    // Financial parameters
    decimal? RevenueImpactLow,
    decimal? RevenueImpactHigh,
    decimal? CostImpactLow,
    decimal? CostImpactHigh,
    decimal? DownsideRisk,
    decimal? UpsidePotential,
    // Trust tier parameters
    string? RequestedTier,
    // Hero workflow
    string? WorkflowType,
    string RequestedBy);

/// <summary>
/// Complete simulation result — what would happen if this action were executed.
/// Nothing was mutated; this is a pure read-only projection.
/// </summary>
public sealed record SimulationResult(
    Guid Id,
    Guid TenantId,
    string ActionType,
    string Title,
    SimulationVerdict Verdict,

    // ── Decision preview ─────────────────────────────────
    SimulatedDecision? Decision,

    // ── Trust tier outcome ────────────────────────────────
    TrustTierEvaluation? TrustTierOutcome,

    // ── Approval requirements ────────────────────────────
    SimulatedApproval? ApprovalRequirement,

    // ── Policy outcomes ──────────────────────────────────
    IReadOnlyList<PolicyOutcome> PolicyOutcomes,

    // ── Economic projection ──────────────────────────────
    SimulatedEconomicEffect? EconomicEffect,

    // ── Workflow preview ─────────────────────────────────
    SimulatedWorkflowPreview? WorkflowPreview,

    // ── Rationale ────────────────────────────────────────
    IReadOnlyList<string> Reasons,

    // ── Metadata ─────────────────────────────────────────
    string SimulatedBy,
    DateTimeOffset SimulatedAtUtc);

public enum SimulationVerdict
{
    /// <summary>Action would proceed without human intervention.</summary>
    Allowed,
    /// <summary>Action would require approval before execution.</summary>
    RequiresApproval,
    /// <summary>Action would be blocked by policy.</summary>
    Blocked,
    /// <summary>Action would be recommended only (no execution).</summary>
    RecommendOnly,
    /// <summary>Action would be observed only (no execution or recommendation).</summary>
    ObserveOnly,
}

/// <summary>
/// What decision would be created (without actually creating it).
/// </summary>
public sealed record SimulatedDecision(
    string Title,
    string Domain,
    DecisionRiskLevel RiskLevel,
    DecisionReversibility Reversibility,
    double Confidence,
    decimal? ExpectedValue,
    bool WouldRequireApproval,
    DecisionStatus ProposedStatus);

/// <summary>
/// Whether approval would be required and from whom.
/// </summary>
public sealed record SimulatedApproval(
    bool Required,
    string? RequiredApproverRole,
    bool RequiresSeparationOfDuties,
    string? MatchedPolicyActionType,
    string Explanation);

/// <summary>
/// Outcome of a single policy evaluation against the proposed action.
/// </summary>
public sealed record PolicyOutcome(
    string PolicyName,
    string PolicyType,
    bool Passed,
    string Explanation);

/// <summary>
/// Projected economic impact without executing.
/// </summary>
public sealed record SimulatedEconomicEffect(
    decimal? RevenueImpactLow,
    decimal? RevenueImpactHigh,
    decimal? CostImpactLow,
    decimal? CostImpactHigh,
    decimal? NetImpactLow,
    decimal? NetImpactHigh,
    decimal? DownsideRisk,
    decimal? UpsidePotential,
    string? Summary);

/// <summary>
/// Preview of what a hero workflow execution would look like.
/// </summary>
public sealed record SimulatedWorkflowPreview(
    string WorkflowType,
    string DisplayName,
    int TotalSteps,
    IReadOnlyList<SimulatedWorkflowStep> Steps);

public sealed record SimulatedWorkflowStep(
    string StepId,
    string Name,
    string Subsystem,
    string ProjectedOutcome);

/// <summary>
/// Summary view for simulation list endpoints.
/// </summary>
public sealed record SimulationSummary(
    Guid Id,
    string ActionType,
    string Title,
    SimulationVerdict Verdict,
    string SimulatedBy,
    DateTimeOffset SimulatedAtUtc);

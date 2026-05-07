namespace ArchonAI.Core.Models.Decisions;

/// <summary>
/// A first-class business decision: the atomic unit of organizational intelligence.
/// Decisions sit above workflows, approvals, and recommendations — they capture
/// why an action is taken, what alternatives were considered, and what the expected
/// economic and risk profile looks like.
/// </summary>
public sealed record DecisionRecord(
    Guid Id,
    Guid TenantId,
    string Title,
    string Domain,
    string Objective,
    IReadOnlyList<string> Constraints,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<DecisionAlternative> Alternatives,
    string RecommendedOptionId,
    double Confidence,
    DecisionReversibility Reversibility,
    DecisionRiskLevel RiskLevel,
    decimal? ExpectedValue,
    bool RequiresApproval,
    IReadOnlyList<DecisionLink> LinkedArtifacts,
    DecisionStatus Status,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// An alternative considered during decision-making.
/// </summary>
public sealed record DecisionAlternative(
    string Id,
    string Title,
    string Rationale,
    IReadOnlyList<string> Pros,
    IReadOnlyList<string> Cons,
    double? EstimatedConfidence,
    decimal? EstimatedValue);

/// <summary>
/// A link from a decision to a downstream artifact (workflow, approval gate, action).
/// </summary>
public sealed record DecisionLink(
    string ArtifactType,
    string ArtifactId,
    string Description,
    DateTimeOffset LinkedAtUtc);

/// <summary>
/// A lifecycle event recorded against a decision.
/// </summary>
public sealed record DecisionLifecycleEvent(
    Guid Id,
    Guid DecisionId,
    string EventType,
    string Actor,
    string? Detail,
    DateTimeOffset OccurredAtUtc);

public enum DecisionStatus
{
    Draft,
    Proposed,
    UnderReview,
    Approved,
    Rejected,
    Executing,
    Completed,
    Superseded
}

public enum DecisionReversibility
{
    FullyReversible,
    PartiallyReversible,
    Irreversible
}

public enum DecisionRiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

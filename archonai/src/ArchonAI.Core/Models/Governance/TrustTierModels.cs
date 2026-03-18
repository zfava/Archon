namespace ArchonAI.Core.Models.Governance;

/// <summary>
/// Execution trust tiers define what ArchonAI is allowed to do autonomously.
/// Each tier is a strict superset of the previous — Tier 3 implies Tier 2 capabilities.
/// </summary>
public enum ExecutionTrustTier
{
    /// <summary>System may observe and record but take no action.</summary>
    ObserveOnly = 0,

    /// <summary>System may recommend actions but not draft or execute.</summary>
    RecommendOnly = 1,

    /// <summary>System may draft actions that require human approval before execution.</summary>
    DraftApprovalRequired = 2,

    /// <summary>System may auto-execute reversible actions within defined bounds.</summary>
    AutoExecuteReversible = 3,

    /// <summary>System may auto-execute high-confidence bounded actions including irreversible ones.</summary>
    AutoExecuteHighConfidence = 4,

    /// <summary>System operates within an approved policy envelope with full autonomy.</summary>
    PolicyEnvelope = 5,
}

/// <summary>
/// A tenant-scoped trust tier policy binding an action scope to a maximum execution tier.
/// </summary>
public sealed record TrustTierPolicy(
    Guid Id,
    string TenantId,
    string ActionScope,
    ExecutionTrustTier MaxTier,
    double? ConfidenceThreshold,
    decimal? ValueCeiling,
    bool RequireReversible,
    string? Description,
    bool IsEnabled,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Result of evaluating an action against trust tier policies.
/// </summary>
public sealed record TrustTierEvaluation(
    string ActionScope,
    ExecutionTrustTier RequestedTier,
    ExecutionTrustTier EffectiveTier,
    bool Allowed,
    string Disposition,
    string? Reason);

/// <summary>
/// Valid dispositions for a trust tier evaluation.
/// </summary>
public static class TrustDisposition
{
    public const string Observe = "observe";
    public const string Recommend = "recommend";
    public const string DraftForApproval = "draft_for_approval";
    public const string AutoExecute = "auto_execute";
    public const string Blocked = "blocked";
}

namespace ArchonAI.Core.Models.ExceptionIntelligence;

// ══════════════════════════════════════════════════════════════
//  Exception category — what kind of operational exception
// ══════════════════════════════════════════════════════════════

public enum ExceptionCategory
{
    Anomaly,
    Failure,
    Drift,
    Bottleneck,
    PolicyViolation,
    ThresholdBreach,
    EscalationRequired,
    InterventionPoint,
}

public enum ExceptionSeverity
{
    Info,
    Warning,
    High,
    Critical,
}

public enum ExceptionStatus
{
    Open,
    Acknowledged,
    InProgress,
    Resolved,
    Dismissed,
}

public enum EscalationLevel
{
    None,
    Operator,
    Manager,
    Executive,
}

// ══════════════════════════════════════════════════════════════
//  Artifact link — what this exception is connected to
// ══════════════════════════════════════════════════════════════

public sealed record ExceptionArtifactLink(
    string ArtifactType,
    string ArtifactId,
    string? Label);

// ══════════════════════════════════════════════════════════════
//  Recommended action — structured resolution guidance
// ══════════════════════════════════════════════════════════════

public sealed record RecommendedAction(
    string ActionType,
    string Description,
    string? TargetArtifactType,
    string? TargetArtifactId,
    string Confidence);

// ══════════════════════════════════════════════════════════════
//  Core exception record
// ══════════════════════════════════════════════════════════════

public sealed record OperationalException(
    Guid Id,
    Guid TenantId,
    ExceptionCategory Category,
    ExceptionSeverity Severity,
    string Title,
    string Description,
    string Domain,
    ExceptionStatus Status,
    // ── Scoring / prioritization ─────────────────────────
    double Urgency,
    double EconomicImpactEstimate,
    double Confidence,
    EscalationLevel EscalationLevel,
    // ── Ownership ────────────────────────────────────────
    string? AssignedTo,
    string? EscalationPath,
    // ── Linkage ──────────────────────────────────────────
    IReadOnlyList<ExceptionArtifactLink> LinkedArtifacts,
    RecommendedAction? RecommendedAction,
    // ── Timestamps ───────────────────────────────────────
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? AcknowledgedAtUtc,
    DateTimeOffset? ResolvedAtUtc);

// ══════════════════════════════════════════════════════════════
//  Priority score — computed ranking for the exception queue
// ══════════════════════════════════════════════════════════════

public sealed record ExceptionPriorityScore(
    Guid ExceptionId,
    double Score,
    string Breakdown);

// ══════════════════════════════════════════════════════════════
//  Exception queue summary
// ══════════════════════════════════════════════════════════════

public sealed record ExceptionQueueSummary(
    int TotalOpen,
    int Critical,
    int High,
    int Warning,
    double TotalEconomicExposure,
    IReadOnlyDictionary<string, int> ByCategory,
    DateTimeOffset GeneratedAtUtc);

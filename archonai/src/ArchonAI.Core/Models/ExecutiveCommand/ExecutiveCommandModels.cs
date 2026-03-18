namespace ArchonAI.Core.Models.ExecutiveCommand;

// ══════════════════════════════════════════════════════════════
//  Executive command summary — single high-signal view
// ══════════════════════════════════════════════════════════════

public sealed record ExecutiveCommandSummary(
    Guid TenantId,
    // ── What needs attention ─────────────────────────────
    ExceptionBrief ExceptionBrief,
    ApprovalBrief ApprovalBrief,
    // ── What changed ─────────────────────────────────────
    CalibrationBrief CalibrationBrief,
    OperationalBrief OperationalBrief,
    // ── What the AI is doing ─────────────────────────────
    TrustTierBrief TrustBrief,
    ScenarioBrief ScenarioBrief,
    // ── Where money is at stake ──────────────────────────
    EconomicBrief EconomicBrief,
    DateTimeOffset GeneratedAtUtc);

// ── Exception brief ────────────────────────────────────────

public sealed record ExceptionBrief(
    int TotalOpen,
    int Critical,
    int High,
    double TotalEconomicExposure,
    IReadOnlyList<ExceptionHeadline> TopExceptions);

public sealed record ExceptionHeadline(
    Guid Id,
    string Severity,
    string Category,
    string Title,
    double PriorityScore,
    double EconomicImpactEstimate,
    string? RecommendedActionType);

// ── Approval brief ─────────────────────────────────────────

public sealed record ApprovalBrief(
    int PendingCount,
    IReadOnlyList<ApprovalHeadline> PendingApprovals);

public sealed record ApprovalHeadline(
    Guid Id,
    string ActionType,
    string RequestedBy,
    string Justification,
    DateTimeOffset RequestedAtUtc);

// ── Calibration brief ──────────────────────────────────────

public sealed record CalibrationBrief(
    int TotalOutcomes,
    int Underperformed,
    double HitRate,
    double MeanVariancePercent,
    IReadOnlyDictionary<string, int> SignalDistribution);

// ── Operational twin brief ─────────────────────────────────

public sealed record OperationalBrief(
    IReadOnlyDictionary<string, int> EntityCounts,
    int ActiveBottlenecks,
    int WarningKpis,
    int TotalDependencies,
    IReadOnlyList<BottleneckHeadline> TopBottlenecks);

public sealed record BottleneckHeadline(
    Guid Id,
    string Severity,
    string Description,
    DateTimeOffset DetectedAtUtc);

// ── Trust tier brief ───────────────────────────────────────

public sealed record TrustTierBrief(
    int TotalPolicies,
    IReadOnlyDictionary<string, string> TierMap);

// ── Scenario brief ─────────────────────────────────────────

public sealed record ScenarioBrief(
    int TotalActive,
    int TotalCompared,
    IReadOnlyList<ScenarioHeadline> RecentScenarios);

public sealed record ScenarioHeadline(
    Guid Id,
    string Title,
    string Type,
    string Status,
    int AssumptionCount,
    int EffectCount,
    DateTimeOffset UpdatedAtUtc);

// ── Economic brief ─────────────────────────────────────────

public sealed record EconomicBrief(
    double ExceptionExposure,
    int DecisionsPendingApproval,
    int OutcomesDrifting,
    int ActiveBottlenecks);

namespace ArchonAI.Core.Models.Optimization;

// ══════════════════════════════════════════════════════════════
//  Inefficiency detection
// ══════════════════════════════════════════════════════════════

public enum InefficiencyType
{
    AgentBottleneck,
    ModelDegradation,
    TaskFailureSpike,
    CostOverrun,
    LatencyRegression,
    ThroughputDecline,
    ResourceUnderutilization,
    CascadingFailure
}

public enum InefficiencySeverity
{
    Low,
    Medium,
    High,
    Critical
}

public sealed record DetectedInefficiency(
    Guid InefficiencyId,
    InefficiencyType Type,
    InefficiencySeverity Severity,
    string Target,
    string Description,
    double CurrentValue,
    double ThresholdValue,
    double DeviationPercent,
    IReadOnlyDictionary<string, string> Evidence,
    DateTimeOffset DetectedAtUtc);

// ══════════════════════════════════════════════════════════════
//  System improvement recommendation
// ══════════════════════════════════════════════════════════════

public enum ImprovementPriority
{
    Low,
    Medium,
    High,
    Urgent
}

public sealed record SystemImprovementRecommendation(
    Guid RecommendationId,
    ImprovementPriority Priority,
    string Category,
    string Target,
    string Title,
    string Description,
    string ProposedAction,
    double EstimatedImpact,
    double Confidence,
    IReadOnlyList<Guid> RelatedInefficiencies,
    IReadOnlyDictionary<string, string> Parameters,
    DateTimeOffset GeneratedAtUtc);

// ══════════════════════════════════════════════════════════════
//  Performance trend tracking
// ══════════════════════════════════════════════════════════════

public sealed record PerformanceTrendPoint(
    double Value,
    DateTimeOffset RecordedAtUtc);

public sealed record PerformanceTrend(
    string MetricName,
    string Target,
    IReadOnlyList<PerformanceTrendPoint> DataPoints,
    double CurrentValue,
    double PreviousValue,
    double ChangePercent,
    string Direction,
    DateTimeOffset AnalyzedAtUtc);

// ══════════════════════════════════════════════════════════════
//  Full continuous improvement report
// ══════════════════════════════════════════════════════════════

public sealed record ContinuousImprovementReport(
    Guid ReportId,
    int CycleNumber,
    double OverallHealthScore,
    double PreviousHealthScore,
    double HealthTrend,
    IReadOnlyList<DetectedInefficiency> Inefficiencies,
    IReadOnlyList<SystemImprovementRecommendation> Recommendations,
    IReadOnlyList<PerformanceTrend> Trends,
    IReadOnlyList<ImprovementAction> ActionsApplied,
    int TotalInefficienciesDetected,
    int CriticalInefficiencies,
    int RecommendationsGenerated,
    DateTimeOffset GeneratedAtUtc);

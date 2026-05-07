namespace ArchonAI.Core.Models.StrategyLibrary;

// ── Strategy template ────────────────────────────────────────────────

/// <summary>
/// A reusable strategy template discovered by the intelligence engine.
/// </summary>
public sealed record StrategyTemplate(
    Guid Id,
    string Name,
    string Description,
    string ObjectiveType,
    string WorkflowTemplate,
    IReadOnlyDictionary<string, string> SuccessMetrics,
    StrategyResourceUsage ResourceUsage,
    StrategyRank Rank,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Resource consumption profile for a strategy.
/// </summary>
public sealed record StrategyResourceUsage(
    double EstimatedCpuSeconds,
    double EstimatedMemoryMb,
    int EstimatedAgentCount,
    double EstimatedCostPerExecution,
    string CostCurrency);

/// <summary>
/// Ranking data computed from execution history.
/// </summary>
public sealed record StrategyRank(
    double SuccessRate,
    double AverageLatencyMs,
    double AverageCost,
    long TotalExecutions,
    double Score,
    DateTimeOffset RankedAtUtc);

// ── Execution record ─────────────────────────────────────────────────

/// <summary>
/// A single recorded execution of a strategy, used to compute rankings.
/// </summary>
public sealed record StrategyExecutionRecord(
    Guid Id,
    Guid StrategyId,
    bool IsSuccess,
    double LatencyMs,
    double Cost,
    IReadOnlyDictionary<string, string> Outcomes,
    DateTimeOffset ExecutedAtUtc);

// ── Comparison result ────────────────────────────────────────────────

/// <summary>
/// Side-by-side comparison of two or more strategies.
/// </summary>
public sealed record StrategyComparisonResult(
    IReadOnlyList<StrategyComparisonEntry> Entries,
    Guid RecommendedStrategyId,
    string RecommendationReason,
    DateTimeOffset ComparedAtUtc);

public sealed record StrategyComparisonEntry(
    Guid StrategyId,
    string Name,
    string ObjectiveType,
    StrategyRank Rank,
    StrategyResourceUsage ResourceUsage);

namespace ArchonAI.Core.Models.Planning;

public enum GoalPriority
{
    Low,
    Medium,
    High,
    Critical
}

public enum GoalStatus
{
    Proposed,
    Approved,
    InProgress,
    Completed,
    Cancelled
}

public enum GoalSource
{
    BusinessSignal,
    StateAnomaly,
    PerformanceTrend,
    Manual
}

public sealed record OperationalGoal(
    Guid GoalId,
    string Title,
    string Description,
    GoalPriority Priority,
    GoalSource Source,
    GoalStatus Status,
    string ExpectedImpact,
    string Department,
    DateTimeOffset Deadline,
    IReadOnlyDictionary<string, string> Context,
    DateTimeOffset CreatedAtUtc);

public sealed record GoalGenerationResult(
    IReadOnlyList<OperationalGoal> GeneratedGoals,
    int SignalsAnalyzed,
    int AnomaliesDetected,
    int TrendsEvaluated,
    DateTimeOffset GeneratedAtUtc);

public sealed record GoalDashboard(
    int TotalGoals,
    int ProposedGoals,
    int InProgressGoals,
    int CompletedGoals,
    IReadOnlyDictionary<GoalPriority, int> GoalsByPriority,
    IReadOnlyDictionary<GoalSource, int> GoalsBySource,
    IReadOnlyList<OperationalGoal> RecentGoals,
    long TotalGenerationRuns,
    DateTimeOffset GeneratedAtUtc);

namespace ArchonAI.Core.Models.Optimization;

public sealed record PerformanceReport(
    IReadOnlyList<AgentEfficiencyRecord> AgentEfficiency,
    IReadOnlyList<ModelAccuracyRecord> ModelAccuracy,
    IReadOnlyList<TaskCompletionRecord> TaskCompletion,
    IReadOnlyList<PerformanceRecommendation> Recommendations,
    double OverallHealthScore,
    DateTimeOffset GeneratedAtUtc);

public sealed record AgentEfficiencyRecord(
    Guid AgentId,
    string AgentName,
    int TasksCompleted,
    int TasksFailed,
    double SuccessRate,
    double AverageExecutionTimeMs,
    decimal AverageCost,
    double EfficiencyScore,
    DateTimeOffset LastActiveUtc);

public sealed record ModelAccuracyRecord(
    string Provider,
    string Model,
    int TotalRequests,
    double SuccessRate,
    double AverageLatencyMs,
    double AverageCost,
    double AccuracyRate,
    double CompositeScore);

public sealed record TaskCompletionRecord(
    string TaskType,
    int TotalTasks,
    int Completed,
    int Failed,
    double CompletionRate,
    double AverageExecutionTimeMs,
    decimal AverageCost);

public sealed record PerformanceRecommendation(
    string Category,
    string Target,
    string Action,
    string Reason,
    double ExpectedImpact,
    DateTimeOffset GeneratedAtUtc);

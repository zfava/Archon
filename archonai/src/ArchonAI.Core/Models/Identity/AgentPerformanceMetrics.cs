namespace ArchonAI.Core.Models.Identity;

public sealed record AgentPerformanceMetrics(
    int TotalExecutions,
    int SuccessfulExecutions,
    int FailedExecutions,
    double AverageExecutionTimeMs,
    decimal TotalCost,
    DateTimeOffset LastExecutionAtUtc);

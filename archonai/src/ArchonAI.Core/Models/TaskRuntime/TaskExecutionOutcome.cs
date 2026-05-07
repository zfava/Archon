namespace ArchonAI.Core.Models.TaskRuntime;

public sealed record TaskExecutionOutcome(
    ExecutionResult Result,
    Guid AgentId,
    double ExecutionTimeMs,
    decimal Cost,
    string ErrorType,
    int AttemptCount,
    DateTimeOffset CompletedAtUtc);

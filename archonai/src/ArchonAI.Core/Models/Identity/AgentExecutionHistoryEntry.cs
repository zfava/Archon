namespace ArchonAI.Core.Models.Identity;

public sealed record AgentExecutionHistoryEntry(
    Guid TaskId,
    bool Success,
    double ExecutionTimeMs,
    decimal Cost,
    string ErrorType,
    DateTimeOffset ExecutedAtUtc);

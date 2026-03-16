namespace ArchonAI.Core.Models.Telemetry;

public sealed record TaskExecutionTelemetry(
    Guid Id,
    Guid ObjectiveId,
    Guid WorkflowId,
    Guid AgentId,
    Guid TaskId,
    double ExecutionTimeMs,
    decimal Cost,
    bool Success,
    string ErrorType,
    DateTimeOffset RecordedAtUtc);

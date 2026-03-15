namespace ArchonAI.Core.Models.Evaluation;

public sealed record EvaluationReport(
    Guid TaskId,
    Guid AgentId,
    double Score,
    bool IsFailure,
    IReadOnlyList<FailurePattern> FailurePatterns,
    double LatencyMs,
    decimal Cost,
    DateTimeOffset EvaluatedAtUtc);

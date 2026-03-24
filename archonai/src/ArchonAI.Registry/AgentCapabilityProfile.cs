namespace ArchonAI.Registry;

public sealed record AgentCapabilityProfile(
    Guid AgentId,
    string AgentName,
    string Version,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Tools,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> SupportedTaskTypes,
    double AverageLatencyMs,
    double P95LatencyMs,
    decimal AverageCost,
    long Executions,
    long SuccessCount,
    long FailureCount,
    double SuccessRate,
    double Throughput,
    DateTimeOffset UpdatedAtUtc,
    bool IsSuspended = false,
    string? SuspendReason = null);

public sealed record AgentPerformanceSnapshot(
    Guid AgentId,
    string AgentName,
    double AverageLatencyMs,
    double P95LatencyMs,
    decimal AverageCost,
    long Executions,
    double SuccessRate,
    double Throughput,
    double Score,
    DateTimeOffset SnapshotAtUtc);

public sealed record AgentSelectionResult(
    Guid AgentId,
    string AgentName,
    double Score,
    double SuccessRate,
    double AverageLatencyMs,
    decimal AverageCost,
    string SelectionReason);

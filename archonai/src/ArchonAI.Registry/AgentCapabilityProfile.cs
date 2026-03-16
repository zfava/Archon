namespace ArchonAI.Registry;

public sealed record AgentCapabilityProfile(
    Guid AgentId,
    string AgentName,
    string Version,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Tools,
    IReadOnlyList<string> Permissions,
    double AverageLatencyMs,
    decimal AverageCost,
    long Executions,
    DateTimeOffset UpdatedAtUtc);

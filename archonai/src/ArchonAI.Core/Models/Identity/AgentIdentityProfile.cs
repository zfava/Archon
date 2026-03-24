namespace ArchonAI.Core.Models.Identity;

public sealed record AgentIdentityProfile(
    Guid AgentId,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Permissions,
    AgentPerformanceMetrics PerformanceMetrics,
    IReadOnlyList<AgentExecutionHistoryEntry> ExecutionHistory,
    DateTimeOffset UpdatedAtUtc);

namespace ArchonAI.Core.Models.AgentRegistry;

// ── Agent registration ───────────────────────────────────────────────

/// <summary>
/// A fully registered agent in the system with its current state.
/// Maps to the <c>agents</c> table.
/// </summary>
public sealed record RegisteredAgent(
    Guid Id,
    string Name,
    string Description,
    string Version,
    RegisteredAgentStatus Status,
    IReadOnlyList<AgentCapabilityRecord> Capabilities,
    IReadOnlyDictionary<string, string> Configuration,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset? LastHeartbeatUtc,
    DateTimeOffset? DisabledAtUtc);

public enum RegisteredAgentStatus
{
    Active,
    Disabled,
    Draining,
    Offline
}

// ── Agent capabilities ───────────────────────────────────────────────

/// <summary>
/// A single capability an agent exposes.
/// Maps to the <c>agent_capabilities</c> table.
/// </summary>
public sealed record AgentCapabilityRecord(
    Guid Id,
    Guid AgentId,
    string Name,
    string Description,
    string Category,
    string Version,
    DateTimeOffset AddedAtUtc);

// ── Agent metrics ────────────────────────────────────────────────────

/// <summary>
/// A point-in-time metrics snapshot for an agent.
/// Maps to the <c>agent_metrics</c> table.
/// </summary>
public sealed record AgentMetricSnapshot(
    Guid Id,
    Guid AgentId,
    long TotalExecutions,
    long SuccessfulExecutions,
    long FailedExecutions,
    double AverageLatencyMs,
    double P95LatencyMs,
    double UptimePercent,
    DateTimeOffset CollectedAtUtc);

/// <summary>Summary view returned by the dashboard endpoint.</summary>
public sealed record AgentRegistryDashboard(
    int TotalAgents,
    int ActiveAgents,
    int DisabledAgents,
    int OfflineAgents,
    IReadOnlyList<AgentMetricSnapshot> RecentMetrics,
    DateTimeOffset GeneratedAtUtc);

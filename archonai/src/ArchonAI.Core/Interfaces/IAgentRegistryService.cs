using ArchonAI.Core.Models.AgentRegistry;

namespace ArchonAI.Core.Interfaces;

/// <summary>
/// Service that maintains the central registry of all agents in the system.
/// </summary>
public interface IAgentRegistryService
{
    // ── Registration ─────────────────────────────────────────────────

    global::System.Threading.Tasks.Task<RegisteredAgent> RegisterAgentAsync(
        string name, string description, string version,
        IReadOnlyList<AgentCapabilityRecord> capabilities,
        IReadOnlyDictionary<string, string>? configuration = null,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<RegisteredAgent?> GetAgentAsync(
        Guid agentId, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<RegisteredAgent>> ListAgentsAsync(
        RegisteredAgentStatus? status = null, string? capability = null,
        int offset = 0, int limit = 50,
        CancellationToken ct = default);

    // ── Capability management ────────────────────────────────────────

    global::System.Threading.Tasks.Task<RegisteredAgent> UpdateCapabilitiesAsync(
        Guid agentId, IReadOnlyList<AgentCapabilityRecord> capabilities,
        CancellationToken ct = default);

    // ── Enable / disable ─────────────────────────────────────────────

    global::System.Threading.Tasks.Task<RegisteredAgent> EnableAgentAsync(
        Guid agentId, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<RegisteredAgent> DisableAgentAsync(
        Guid agentId, string reason, CancellationToken ct = default);

    // ── Heartbeat ────────────────────────────────────────────────────

    global::System.Threading.Tasks.Task RecordHeartbeatAsync(
        Guid agentId, CancellationToken ct = default);

    // ── Metrics ──────────────────────────────────────────────────────

    global::System.Threading.Tasks.Task<AgentMetricSnapshot> RecordMetricsAsync(
        Guid agentId, long totalExecutions, long successfulExecutions,
        long failedExecutions, double averageLatencyMs, double p95LatencyMs,
        double uptimePercent, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<AgentMetricSnapshot>> GetMetricsAsync(
        Guid agentId, int limit = 20, CancellationToken ct = default);

    // ── Dashboard ────────────────────────────────────────────────────

    global::System.Threading.Tasks.Task<AgentRegistryDashboard> GetDashboardAsync(
        CancellationToken ct = default);

    // ── Deregistration ───────────────────────────────────────────────

    global::System.Threading.Tasks.Task DeregisterAgentAsync(
        Guid agentId, CancellationToken ct = default);
}

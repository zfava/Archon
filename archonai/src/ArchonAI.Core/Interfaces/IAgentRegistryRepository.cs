using ArchonAI.Core.Models.AgentRegistry;

namespace ArchonAI.Core.Interfaces;

/// <summary>
/// Data access layer for the agent registry.
/// </summary>
public interface IAgentRegistryRepository
{
    // ── Agents ───────────────────────────────────────────────────────

    global::System.Threading.Tasks.Task<RegisteredAgent> UpsertAgentAsync(
        RegisteredAgent agent, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<RegisteredAgent?> GetAgentAsync(
        Guid agentId, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<RegisteredAgent>> ListAgentsAsync(
        RegisteredAgentStatus? status, string? capability,
        int offset, int limit, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<bool> RemoveAgentAsync(
        Guid agentId, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<int> CountAgentsAsync(
        RegisteredAgentStatus? status = null, CancellationToken ct = default);

    // ── Metrics ──────────────────────────────────────────────────────

    global::System.Threading.Tasks.Task AddMetricAsync(
        AgentMetricSnapshot metric, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<AgentMetricSnapshot>> GetMetricsAsync(
        Guid agentId, int limit, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<AgentMetricSnapshot>> GetRecentMetricsAsync(
        int limit = 50, CancellationToken ct = default);
}

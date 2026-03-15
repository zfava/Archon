using ArchonAI.Core.Models;

namespace ArchonAI.Registry;

public interface IAgentCapabilityRegistry
{
    global::System.Threading.Tasks.Task RegisterOrUpdateAgentAsync(
        Agent agent,
        IReadOnlyList<string> tools,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task ReportExecutionAsync(
        Guid agentId,
        double latencyMs,
        decimal cost,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<AgentCapabilityProfile>> QueryByCapabilityAsync(
        string capability,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<AgentCapabilityProfile?> GetAgentAsync(Guid agentId, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<AgentCapabilityProfile>> GetAllAsync(CancellationToken cancellationToken = default);
}

using ArchonAI.Core.Models;

namespace ArchonAI.Registry;

public interface IAgentCapabilityRegistry
{
    global::System.Threading.Tasks.Task RegisterOrUpdateAgentAsync(
        Agent agent,
        IReadOnlyList<string> tools,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task RegisterSupportedTaskTypesAsync(
        Guid agentId,
        IReadOnlyList<string> taskTypes,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task ReportExecutionAsync(
        Guid agentId,
        string taskType,
        bool success,
        double latencyMs,
        decimal cost,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<AgentCapabilityProfile>> QueryByCapabilityAsync(
        string capability,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<AgentCapabilityProfile>> QueryByTaskTypeAsync(
        string taskType,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<AgentSelectionResult?> SelectBestAgentAsync(
        string requiredCapability,
        string? taskType,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<AgentCapabilityProfile?> GetAgentAsync(
        Guid agentId,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<AgentCapabilityProfile>> GetAllAsync(
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<AgentPerformanceSnapshot?> GetPerformanceSnapshotAsync(
        Guid agentId,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task SuspendAgentAsync(
        Guid agentId,
        string reason,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task ReinstateAgentAsync(
        Guid agentId,
        CancellationToken cancellationToken = default);
}

using ArchonAI.Core.Models.Admin;

namespace ArchonAI.Core.Interfaces;

public interface IAdminService
{
    global::System.Threading.Tasks.Task<IReadOnlyList<AgentInfo>> GetAgentsAsync(CancellationToken cancellationToken = default);
    global::System.Threading.Tasks.Task<AgentInfo?> GetAgentAsync(Guid agentId, CancellationToken cancellationToken = default);
    global::System.Threading.Tasks.Task SetAgentEnabledAsync(Guid agentId, bool enabled, CancellationToken cancellationToken = default);
    global::System.Threading.Tasks.Task<IReadOnlyList<WorkflowInfo>> GetWorkflowsAsync(CancellationToken cancellationToken = default);
    global::System.Threading.Tasks.Task<WorkflowInfo?> GetWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default);
    global::System.Threading.Tasks.Task CancelWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default);
    global::System.Threading.Tasks.Task<PolicyConfiguration> GetPolicyConfigAsync(CancellationToken cancellationToken = default);
    global::System.Threading.Tasks.Task UpdatePolicyConfigAsync(PolicyConfiguration policy, CancellationToken cancellationToken = default);
    global::System.Threading.Tasks.Task<SystemMonitoringSnapshot> GetSystemSnapshotAsync(CancellationToken cancellationToken = default);
    AdminServiceStatus GetStatus();
}

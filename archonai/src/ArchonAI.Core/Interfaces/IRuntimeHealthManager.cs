using ArchonAI.Core.Models.RuntimeHealth;

namespace ArchonAI.Core.Interfaces;

public interface IRuntimeHealthManager
{
    global::System.Threading.Tasks.Task RecordHeartbeatAsync(Guid agentId, string agentName);
    global::System.Threading.Tasks.Task RecordAgentFailureAsync(Guid agentId, string agentName, string reason);
    global::System.Threading.Tasks.Task RecordTaskStuckAsync(Guid taskId, Guid agentId, string taskName);
    global::System.Threading.Tasks.Task RecordTaskCompletedAsync(Guid agentId);
    global::System.Threading.Tasks.Task<RuntimeHealthSnapshot> GetHealthSnapshotAsync();
    global::System.Threading.Tasks.Task<IReadOnlyList<RecoveryEvent>> GetRecoveryHistoryAsync(int limit = 50);
    global::System.Threading.Tasks.Task RunHealthCheckAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<RecoveryPolicy> GetRecoveryPolicies();
}

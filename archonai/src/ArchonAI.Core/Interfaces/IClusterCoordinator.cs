using ArchonAI.Core.Models.Cluster;

namespace ArchonAI.Core.Interfaces;

public interface IClusterCoordinator
{
    // ── Node registration ──────────────────────────────────────
    global::System.Threading.Tasks.Task<ClusterNode> RegisterNodeAsync(
        string hostName, string role, NodeCapacity capacity,
        IReadOnlyList<string> capabilities, IReadOnlyDictionary<string, string>? labels = null,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<ClusterNode> HeartbeatAsync(
        Guid nodeId, NodeLoad currentLoad,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task DrainNodeAsync(
        Guid nodeId, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task RemoveNodeAsync(
        Guid nodeId, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<ClusterNode?> GetNodeAsync(
        Guid nodeId, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<ClusterNode>> ListNodesAsync(
        ClusterNodeStatus? status = null,
        CancellationToken cancellationToken = default);

    // ── Cluster-aware scheduling ───────────────────────────────
    global::System.Threading.Tasks.Task<ClusterScheduleResult> ScheduleOnClusterAsync(
        ClusterScheduleRequest request,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<ClusterScheduleResult>> ScheduleBatchAsync(
        IReadOnlyList<ClusterScheduleRequest> requests,
        CancellationToken cancellationToken = default);

    // ── Workload distribution ──────────────────────────────────
    global::System.Threading.Tasks.Task<WorkloadDistributionPlan> GenerateDistributionPlanAsync(
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task RebalanceAsync(
        CancellationToken cancellationToken = default);

    // ── Status ─────────────────────────────────────────────────
    global::System.Threading.Tasks.Task<ClusterStatus> GetClusterStatusAsync(
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<ClusterDashboard> GetDashboardAsync(
        CancellationToken cancellationToken = default);
}

namespace ArchonAI.Core.Models.Cluster;

// ── Node registration ─────────────────────────────────────────────

public sealed record ClusterNode(
    Guid NodeId,
    string HostName,
    string Role,
    ClusterNodeStatus Status,
    NodeCapacity Capacity,
    NodeLoad CurrentLoad,
    IReadOnlyList<string> Capabilities,
    IReadOnlyDictionary<string, string> Labels,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset LastHeartbeatUtc,
    DateTimeOffset? DrainedAtUtc);

public enum ClusterNodeStatus
{
    Joining,
    Active,
    Draining,
    Offline,
    Removed
}

public sealed record NodeCapacity(
    int MaxConcurrentTasks,
    int MaxAgents,
    double AvailableCpuCores,
    long AvailableMemoryBytes,
    int GpuSlots);

public sealed record NodeLoad(
    int ActiveTasks,
    int QueuedTasks,
    int ActiveAgents,
    double CpuUtilizationPercent,
    double MemoryUtilizationPercent,
    int GpuSlotsUsed,
    DateTimeOffset MeasuredAtUtc);

// ── Workload distribution ──────────────────────────────────────────

public sealed record WorkloadAssignment(
    Guid AssignmentId,
    Guid TaskId,
    Guid TargetNodeId,
    string TargetHostName,
    string Strategy,
    double Score,
    DateTimeOffset AssignedAtUtc);

public sealed record WorkloadDistributionPlan(
    IReadOnlyList<WorkloadAssignment> Assignments,
    IReadOnlyList<WorkloadRebalanceAction> RebalanceActions,
    string Strategy,
    DateTimeOffset GeneratedAtUtc);

public sealed record WorkloadRebalanceAction(
    Guid TaskId,
    Guid FromNodeId,
    Guid ToNodeId,
    string Reason,
    double ExpectedImprovement);

// ── Cluster status ────────────────────────────────────────────────

public sealed record ClusterStatus(
    Guid ClusterId,
    string ClusterName,
    int TotalNodes,
    int ActiveNodes,
    int DrainingNodes,
    int OfflineNodes,
    int TotalActiveTasks,
    int TotalQueuedTasks,
    double AverageCpuUtilization,
    double AverageMemoryUtilization,
    string HealthStatus,
    DateTimeOffset EvaluatedAtUtc);

public sealed record ClusterDashboard(
    ClusterStatus Status,
    IReadOnlyList<ClusterNode> Nodes,
    IReadOnlyList<WorkloadAssignment> RecentAssignments,
    DateTimeOffset GeneratedAtUtc);

// ── Cluster-aware scheduling ──────────────────────────────────────

public sealed record ClusterScheduleRequest(
    Guid TaskId,
    string RequiredCapability,
    int Priority,
    IReadOnlyDictionary<string, string> Constraints,
    bool PreferLocalNode);

public sealed record ClusterScheduleResult(
    Guid TaskId,
    Guid? AssignedNodeId,
    string? AssignedHostName,
    bool IsScheduled,
    string Reason,
    double ScheduleScore,
    DateTimeOffset ScheduledAtUtc);

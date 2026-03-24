using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Cluster;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Infrastructure.Cluster;

public sealed class ClusterCoordinator : IClusterCoordinator
{
    private readonly ConcurrentDictionary<Guid, ClusterNode> _nodes = new();
    private readonly ConcurrentDictionary<Guid, WorkloadAssignment> _recentAssignments = new();
    private readonly ClusterOptions _options;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ClusterCoordinator> _logger;
    private readonly Guid _clusterId = Guid.NewGuid();

    private const int MaxRecentAssignments = 500;

    public ClusterCoordinator(
        IOptions<ClusterOptions> options,
        IEventBus eventBus,
        ILogger<ClusterCoordinator> logger)
    {
        _options = options.Value;
        _eventBus = eventBus;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  Node registration
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<ClusterNode> RegisterNodeAsync(
        string hostName, string role, NodeCapacity capacity,
        IReadOnlyList<string> capabilities, IReadOnlyDictionary<string, string>? labels = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int activeCount = _nodes.Values.Count(n => n.Status == ClusterNodeStatus.Active);
        if (activeCount >= _options.MaxNodes)
            throw new InvalidOperationException(
                $"Cluster node limit ({_options.MaxNodes}) reached. Cannot register '{hostName}'.");

        var now = DateTimeOffset.UtcNow;
        var node = new ClusterNode(
            NodeId: Guid.NewGuid(),
            HostName: hostName,
            Role: role,
            Status: ClusterNodeStatus.Active,
            Capacity: capacity,
            CurrentLoad: new NodeLoad(0, 0, 0, 0, 0, 0, now),
            Capabilities: capabilities,
            Labels: labels ?? new Dictionary<string, string>(),
            RegisteredAtUtc: now,
            LastHeartbeatUtc: now,
            DrainedAtUtc: null);

        _nodes[node.NodeId] = node;

        _logger.LogInformation(
            "Node registered: {NodeId} '{HostName}' role={Role} maxTasks={MaxTasks}",
            node.NodeId, hostName, role, capacity.MaxConcurrentTasks);

        await EmitEventAsync("cluster.node.registered", node.NodeId.ToString(),
            $"Node '{hostName}' registered with role '{role}'");

        return node;
    }

    public async global::System.Threading.Tasks.Task<ClusterNode> HeartbeatAsync(
        Guid nodeId, NodeLoad currentLoad,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_nodes.TryGetValue(nodeId, out var existing))
            throw new InvalidOperationException($"Node {nodeId} not found");

        if (existing.Status == ClusterNodeStatus.Removed)
            throw new InvalidOperationException($"Node {nodeId} has been removed");

        var updated = existing with
        {
            CurrentLoad = currentLoad,
            LastHeartbeatUtc = DateTimeOffset.UtcNow
        };

        _nodes[nodeId] = updated;
        return await global::System.Threading.Tasks.Task.FromResult(updated);
    }

    public async global::System.Threading.Tasks.Task DrainNodeAsync(
        Guid nodeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_nodes.TryGetValue(nodeId, out var existing))
            throw new InvalidOperationException($"Node {nodeId} not found");

        var draining = existing with
        {
            Status = ClusterNodeStatus.Draining,
            DrainedAtUtc = DateTimeOffset.UtcNow
        };

        _nodes[nodeId] = draining;

        _logger.LogWarning("Node draining: {NodeId} '{HostName}'", nodeId, existing.HostName);
        await EmitEventAsync("cluster.node.draining", nodeId.ToString(),
            $"Node '{existing.HostName}' is draining");
    }

    public async global::System.Threading.Tasks.Task RemoveNodeAsync(
        Guid nodeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_nodes.TryGetValue(nodeId, out var existing))
            throw new InvalidOperationException($"Node {nodeId} not found");

        var removed = existing with { Status = ClusterNodeStatus.Removed };
        _nodes[nodeId] = removed;

        _logger.LogWarning("Node removed: {NodeId} '{HostName}'", nodeId, existing.HostName);
        await EmitEventAsync("cluster.node.removed", nodeId.ToString(),
            $"Node '{existing.HostName}' removed from cluster");
    }

    public global::System.Threading.Tasks.Task<ClusterNode?> GetNodeAsync(
        Guid nodeId, CancellationToken cancellationToken = default)
    {
        _nodes.TryGetValue(nodeId, out var node);
        return global::System.Threading.Tasks.Task.FromResult(node);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<ClusterNode>> ListNodesAsync(
        ClusterNodeStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<ClusterNode> query = _nodes.Values;
        if (status is not null)
            query = query.Where(n => n.Status == status.Value);

        IReadOnlyList<ClusterNode> result = query
            .OrderBy(n => n.HostName)
            .ToList();

        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    // ══════════════════════════════════════════════════════════════
    //  Cluster-aware scheduling
    // ══════════════════════════════════════════════════════════════

    public global::System.Threading.Tasks.Task<ClusterScheduleResult> ScheduleOnClusterAsync(
        ClusterScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = GetSchedulableCandidates(request);

        if (candidates.Count == 0)
        {
            return global::System.Threading.Tasks.Task.FromResult(new ClusterScheduleResult(
                TaskId: request.TaskId,
                AssignedNodeId: null,
                AssignedHostName: null,
                IsScheduled: false,
                Reason: "No eligible nodes available for the requested capability.",
                ScheduleScore: 0,
                ScheduledAtUtc: DateTimeOffset.UtcNow));
        }

        var (bestNode, score) = SelectBestNode(candidates, request);

        var assignment = new WorkloadAssignment(
            AssignmentId: Guid.NewGuid(),
            TaskId: request.TaskId,
            TargetNodeId: bestNode.NodeId,
            TargetHostName: bestNode.HostName,
            Strategy: _options.SchedulingStrategy,
            Score: score,
            AssignedAtUtc: DateTimeOffset.UtcNow);

        TrackAssignment(assignment);

        return global::System.Threading.Tasks.Task.FromResult(new ClusterScheduleResult(
            TaskId: request.TaskId,
            AssignedNodeId: bestNode.NodeId,
            AssignedHostName: bestNode.HostName,
            IsScheduled: true,
            Reason: $"Scheduled on '{bestNode.HostName}' via {_options.SchedulingStrategy} strategy.",
            ScheduleScore: score,
            ScheduledAtUtc: DateTimeOffset.UtcNow));
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<ClusterScheduleResult>> ScheduleBatchAsync(
        IReadOnlyList<ClusterScheduleRequest> requests,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ClusterScheduleResult>();

        // Sort by priority descending so high-priority tasks get first pick
        var ordered = requests.OrderByDescending(r => r.Priority).ToList();

        foreach (var request in ordered)
        {
            var result = await ScheduleOnClusterAsync(request, cancellationToken);
            results.Add(result);
        }

        return results;
    }

    // ══════════════════════════════════════════════════════════════
    //  Workload distribution
    // ══════════════════════════════════════════════════════════════

    public global::System.Threading.Tasks.Task<WorkloadDistributionPlan> GenerateDistributionPlanAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var activeNodes = _nodes.Values
            .Where(n => n.Status == ClusterNodeStatus.Active)
            .ToList();

        var rebalanceActions = new List<WorkloadRebalanceAction>();

        if (activeNodes.Count < 2)
        {
            return global::System.Threading.Tasks.Task.FromResult(new WorkloadDistributionPlan(
                Assignments: Array.Empty<WorkloadAssignment>(),
                RebalanceActions: rebalanceActions,
                Strategy: _options.SchedulingStrategy,
                GeneratedAtUtc: DateTimeOffset.UtcNow));
        }

        double avgLoad = activeNodes.Average(n => n.CurrentLoad.CpuUtilizationPercent);
        double threshold = _options.RebalanceThresholdPercent;

        var overloaded = activeNodes
            .Where(n => n.CurrentLoad.CpuUtilizationPercent > avgLoad + threshold)
            .OrderByDescending(n => n.CurrentLoad.CpuUtilizationPercent)
            .ToList();

        var underloaded = activeNodes
            .Where(n => n.CurrentLoad.CpuUtilizationPercent < avgLoad - threshold)
            .OrderBy(n => n.CurrentLoad.CpuUtilizationPercent)
            .ToList();

        int underIdx = 0;
        foreach (var hotNode in overloaded)
        {
            if (underIdx >= underloaded.Count) break;

            int excessTasks = Math.Max(1,
                hotNode.CurrentLoad.ActiveTasks - (int)(hotNode.Capacity.MaxConcurrentTasks * 0.7));

            for (int i = 0; i < excessTasks && underIdx < underloaded.Count; i++)
            {
                var coldNode = underloaded[underIdx];
                if (coldNode.CurrentLoad.ActiveTasks >= coldNode.Capacity.MaxConcurrentTasks)
                {
                    underIdx++;
                    if (underIdx >= underloaded.Count) break;
                    coldNode = underloaded[underIdx];
                }

                double improvement = (hotNode.CurrentLoad.CpuUtilizationPercent -
                                      coldNode.CurrentLoad.CpuUtilizationPercent) / 100.0;

                rebalanceActions.Add(new WorkloadRebalanceAction(
                    TaskId: Guid.NewGuid(), // placeholder - real task IDs come from the scheduler
                    FromNodeId: hotNode.NodeId,
                    ToNodeId: coldNode.NodeId,
                    Reason: $"Rebalance from {hotNode.HostName} (CPU {hotNode.CurrentLoad.CpuUtilizationPercent:F1}%) " +
                            $"to {coldNode.HostName} (CPU {coldNode.CurrentLoad.CpuUtilizationPercent:F1}%)",
                    ExpectedImprovement: improvement));
            }
        }

        return global::System.Threading.Tasks.Task.FromResult(new WorkloadDistributionPlan(
            Assignments: _recentAssignments.Values.OrderByDescending(a => a.AssignedAtUtc).Take(50).ToList(),
            RebalanceActions: rebalanceActions,
            Strategy: _options.SchedulingStrategy,
            GeneratedAtUtc: DateTimeOffset.UtcNow));
    }

    public async global::System.Threading.Tasks.Task RebalanceAsync(
        CancellationToken cancellationToken = default)
    {
        var plan = await GenerateDistributionPlanAsync(cancellationToken);

        if (plan.RebalanceActions.Count > 0)
        {
            _logger.LogInformation(
                "Cluster rebalance: {ActionCount} actions proposed",
                plan.RebalanceActions.Count);

            await EmitEventAsync("cluster.rebalance.executed",
                _clusterId.ToString(),
                $"Rebalanced {plan.RebalanceActions.Count} workloads across cluster");
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Status
    // ══════════════════════════════════════════════════════════════

    public global::System.Threading.Tasks.Task<ClusterStatus> GetClusterStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var allNodes = _nodes.Values.ToList();
        var active = allNodes.Where(n => n.Status == ClusterNodeStatus.Active).ToList();
        var draining = allNodes.Count(n => n.Status == ClusterNodeStatus.Draining);
        var offline = allNodes.Count(n => n.Status == ClusterNodeStatus.Offline);

        double avgCpu = active.Count > 0
            ? active.Average(n => n.CurrentLoad.CpuUtilizationPercent) : 0;
        double avgMem = active.Count > 0
            ? active.Average(n => n.CurrentLoad.MemoryUtilizationPercent) : 0;
        int totalActive = active.Sum(n => n.CurrentLoad.ActiveTasks);
        int totalQueued = active.Sum(n => n.CurrentLoad.QueuedTasks);

        string health = avgCpu < 70 && avgMem < 80 ? "healthy"
            : avgCpu < 90 && avgMem < 90 ? "degraded"
            : "critical";

        if (active.Count == 0 && allNodes.Count > 0) health = "critical";

        return global::System.Threading.Tasks.Task.FromResult(new ClusterStatus(
            ClusterId: _clusterId,
            ClusterName: _options.ClusterName,
            TotalNodes: allNodes.Count(n => n.Status != ClusterNodeStatus.Removed),
            ActiveNodes: active.Count,
            DrainingNodes: draining,
            OfflineNodes: offline,
            TotalActiveTasks: totalActive,
            TotalQueuedTasks: totalQueued,
            AverageCpuUtilization: Math.Round(avgCpu, 1),
            AverageMemoryUtilization: Math.Round(avgMem, 1),
            HealthStatus: health,
            EvaluatedAtUtc: DateTimeOffset.UtcNow));
    }

    public async global::System.Threading.Tasks.Task<ClusterDashboard> GetDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        var status = await GetClusterStatusAsync(cancellationToken);
        var nodes = await ListNodesAsync(null, cancellationToken);
        var recent = _recentAssignments.Values
            .OrderByDescending(a => a.AssignedAtUtc)
            .Take(50)
            .ToList();

        return new ClusterDashboard(
            Status: status,
            Nodes: nodes.Where(n => n.Status != ClusterNodeStatus.Removed).ToList(),
            RecentAssignments: recent,
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    // ══════════════════════════════════════════════════════════════
    //  Private helpers
    // ══════════════════════════════════════════════════════════════

    private List<ClusterNode> GetSchedulableCandidates(ClusterScheduleRequest request)
    {
        return _nodes.Values
            .Where(n => n.Status == ClusterNodeStatus.Active)
            .Where(n => n.CurrentLoad.ActiveTasks < n.Capacity.MaxConcurrentTasks)
            .Where(n => string.IsNullOrEmpty(request.RequiredCapability) ||
                        n.Capabilities.Contains(request.RequiredCapability, StringComparer.OrdinalIgnoreCase))
            .Where(n => MatchesConstraints(n, request.Constraints))
            .ToList();
    }

    private (ClusterNode node, double score) SelectBestNode(
        List<ClusterNode> candidates, ClusterScheduleRequest request)
    {
        return _options.SchedulingStrategy.ToLowerInvariant() switch
        {
            "round-robin" => SelectRoundRobin(candidates),
            "least-loaded" => SelectLeastLoaded(candidates),
            "bin-packing" => SelectBinPacking(candidates),
            _ => SelectLeastLoaded(candidates)
        };
    }

    private static (ClusterNode, double) SelectLeastLoaded(List<ClusterNode> candidates)
    {
        var best = candidates
            .OrderBy(n => n.CurrentLoad.CpuUtilizationPercent * 0.6 +
                          n.CurrentLoad.MemoryUtilizationPercent * 0.4)
            .First();

        double loadRatio = (best.CurrentLoad.CpuUtilizationPercent * 0.6 +
                            best.CurrentLoad.MemoryUtilizationPercent * 0.4) / 100.0;
        return (best, 1.0 - loadRatio);
    }

    private static (ClusterNode, double) SelectBinPacking(List<ClusterNode> candidates)
    {
        // Pack tasks into nodes that are already busy (opposite of least-loaded)
        var best = candidates
            .Where(n => n.CurrentLoad.ActiveTasks < n.Capacity.MaxConcurrentTasks)
            .OrderByDescending(n => n.CurrentLoad.ActiveTasks)
            .ThenByDescending(n => n.CurrentLoad.CpuUtilizationPercent)
            .First();

        double utilizationRatio = (double)best.CurrentLoad.ActiveTasks / Math.Max(1, best.Capacity.MaxConcurrentTasks);
        return (best, utilizationRatio);
    }

    private (ClusterNode, double) SelectRoundRobin(List<ClusterNode> candidates)
    {
        int assignmentCount = _recentAssignments.Count;
        int index = assignmentCount % candidates.Count;
        var selected = candidates[index];
        return (selected, 0.5);
    }

    private static bool MatchesConstraints(ClusterNode node, IReadOnlyDictionary<string, string> constraints)
    {
        foreach (var (key, value) in constraints)
        {
            if (key.Equals("role", StringComparison.OrdinalIgnoreCase))
            {
                if (!node.Role.Equals(value, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            else if (key.StartsWith("label:", StringComparison.OrdinalIgnoreCase))
            {
                string labelKey = key[6..];
                if (!node.Labels.TryGetValue(labelKey, out string? labelVal) ||
                    !labelVal.Equals(value, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            else if (key.Equals("minCpu", StringComparison.OrdinalIgnoreCase) &&
                     double.TryParse(value, out double minCpu))
            {
                if (node.Capacity.AvailableCpuCores < minCpu) return false;
            }
            else if (key.Equals("minMemoryMb", StringComparison.OrdinalIgnoreCase) &&
                     long.TryParse(value, out long minMem))
            {
                if (node.Capacity.AvailableMemoryBytes < minMem * 1024 * 1024) return false;
            }
            else if (key.Equals("requireGpu", StringComparison.OrdinalIgnoreCase) &&
                     value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                if (node.Capacity.GpuSlots == 0 ||
                    node.CurrentLoad.GpuSlotsUsed >= node.Capacity.GpuSlots)
                    return false;
            }
        }

        return true;
    }

    private void TrackAssignment(WorkloadAssignment assignment)
    {
        _recentAssignments[assignment.AssignmentId] = assignment;

        // Evict old assignments
        if (_recentAssignments.Count > MaxRecentAssignments)
        {
            var oldest = _recentAssignments.Values
                .OrderBy(a => a.AssignedAtUtc)
                .Take(_recentAssignments.Count - MaxRecentAssignments)
                .ToList();

            foreach (var old in oldest)
                _recentAssignments.TryRemove(old.AssignmentId, out _);
        }
    }

    private async global::System.Threading.Tasks.Task EmitEventAsync(
        string eventType, string resourceId, string description)
    {
        try
        {
            await _eventBus.PublishAsync(new Core.Models.SystemEvent(
                Guid.NewGuid(), eventType, nameof(ClusterCoordinator),
                Guid.NewGuid(),
                new Dictionary<string, string>
                {
                    ["resourceId"] = resourceId,
                    ["description"] = description
                },
                DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit cluster event {EventType}", eventType);
        }
    }
}

namespace ArchonAI.Core.Models.Planning;

public enum TaskGraphNodeStatus
{
    Pending,
    Ready,
    Running,
    Completed,
    Failed,
    Skipped
}

public sealed record TaskGraphNode(
    Guid NodeId,
    string Name,
    string Description,
    string AgentType,
    IReadOnlyDictionary<string, string> RequiredInputs,
    string ExpectedOutput,
    int Priority,
    double EstimatedDurationHours,
    TaskGraphNodeStatus Status,
    DateTimeOffset CreatedAtUtc);

public sealed record TaskGraphEdge(
    Guid EdgeId,
    Guid SourceNodeId,
    Guid TargetNodeId,
    string OutputToInputMapping,
    bool IsRequired);

public sealed record TaskGraph(
    Guid GraphId,
    Guid GoalId,
    string GoalTitle,
    IReadOnlyList<TaskGraphNode> Nodes,
    IReadOnlyList<TaskGraphEdge> Edges,
    string Strategy,
    DateTimeOffset CreatedAtUtc)
{
    public IReadOnlyList<TaskGraphNode> GetRootNodes()
    {
        var targetIds = new HashSet<Guid>(Edges.Select(e => e.TargetNodeId));
        return Nodes.Where(n => !targetIds.Contains(n.NodeId)).ToList();
    }

    public IReadOnlyList<TaskGraphNode> GetDependencies(Guid nodeId)
    {
        var depIds = new HashSet<Guid>(
            Edges.Where(e => e.TargetNodeId == nodeId).Select(e => e.SourceNodeId));
        return Nodes.Where(n => depIds.Contains(n.NodeId)).ToList();
    }

    public IReadOnlyList<TaskGraphNode> GetDependents(Guid nodeId)
    {
        var depIds = new HashSet<Guid>(
            Edges.Where(e => e.SourceNodeId == nodeId).Select(e => e.TargetNodeId));
        return Nodes.Where(n => depIds.Contains(n.NodeId)).ToList();
    }

    public IReadOnlyList<IReadOnlyList<TaskGraphNode>> GetExecutionLayers()
    {
        var layers = new List<IReadOnlyList<TaskGraphNode>>();
        var completed = new HashSet<Guid>();
        var remaining = new HashSet<Guid>(Nodes.Select(n => n.NodeId));
        var nodeMap = Nodes.ToDictionary(n => n.NodeId);

        while (remaining.Count > 0)
        {
            var layer = remaining
                .Where(id =>
                {
                    var deps = Edges
                        .Where(e => e.TargetNodeId == id && e.IsRequired)
                        .Select(e => e.SourceNodeId);
                    return deps.All(completed.Contains);
                })
                .Select(id => nodeMap[id])
                .OrderBy(n => n.Priority)
                .ToList();

            if (layer.Count == 0)
                break;

            layers.Add(layer);
            foreach (var node in layer)
            {
                completed.Add(node.NodeId);
                remaining.Remove(node.NodeId);
            }
        }

        return layers;
    }
}

public sealed record TaskGraphDispatchResult(
    Guid GraphId,
    Guid WorkflowId,
    int TotalTasks,
    int ExecutionLayers,
    IReadOnlyList<Guid> ScheduledTaskIds,
    DateTimeOffset DispatchedAtUtc);

using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace ArchonAI.WorkflowDesigner;

/// <summary>
/// Service for creating, validating, simulating, and exporting workflow graphs
/// that coordinate agents, tools, decisions, and conditions.
/// </summary>
public sealed class WorkflowDesignerService : IWorkflowDesignerService
{
    private readonly ConcurrentDictionary<Guid, WorkflowGraph> _graphs = new();
    private readonly ILogger<WorkflowDesignerService> _logger;

    public WorkflowDesignerService(ILogger<WorkflowDesignerService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public global::System.Threading.Tasks.Task<WorkflowGraph> CreateWorkflowGraphAsync(
        string name,
        string description,
        IReadOnlyList<WorkflowNode> nodes,
        IReadOnlyList<WorkflowEdge> edges,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var graph = new WorkflowGraph(
            Guid.NewGuid(),
            name,
            description,
            nodes,
            edges,
            metadata ?? new Dictionary<string, string>(),
            DateTimeOffset.UtcNow);

        _graphs[graph.Id] = graph;

        _logger.LogInformation(
            "Workflow graph created: {GraphId} '{Name}' with {NodeCount} nodes and {EdgeCount} edges",
            graph.Id, name, nodes.Count, edges.Count);

        return global::System.Threading.Tasks.Task.FromResult(graph);
    }

    /// <inheritdoc />
    public global::System.Threading.Tasks.Task<WorkflowGraph?> GetWorkflowGraphAsync(
        Guid graphId, CancellationToken ct = default)
    {
        _graphs.TryGetValue(graphId, out var graph);
        return global::System.Threading.Tasks.Task.FromResult(graph);
    }

    /// <inheritdoc />
    public global::System.Threading.Tasks.Task<IReadOnlyList<WorkflowGraph>> ListWorkflowGraphsAsync(
        string? nameFilter = null, int offset = 0, int limit = 50, CancellationToken ct = default)
    {
        IEnumerable<WorkflowGraph> query = _graphs.Values.OrderByDescending(g => g.CreatedAtUtc);

        if (!string.IsNullOrWhiteSpace(nameFilter))
            query = query.Where(g => g.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<WorkflowGraph> result = query.Skip(offset).Take(limit).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    /// <inheritdoc />
    public global::System.Threading.Tasks.Task<WorkflowGraphValidationResult> ValidateWorkflowGraphAsync(
        Guid graphId, CancellationToken ct = default)
    {
        if (!_graphs.TryGetValue(graphId, out var graph))
        {
            return global::System.Threading.Tasks.Task.FromResult(
                new WorkflowGraphValidationResult(graphId, false,
                    new[] { "Workflow graph not found" }, Array.Empty<string>(), DateTimeOffset.UtcNow));
        }

        var errors = new List<string>();
        var warnings = new List<string>();

        ValidateStructure(graph, errors);
        ValidateNodeCompatibility(graph, errors, warnings);
        DetectInvalidLoops(graph, errors);

        bool isValid = errors.Count == 0;

        _logger.LogInformation(
            "Workflow graph {GraphId} validated: IsValid={IsValid}, Errors={ErrorCount}, Warnings={WarningCount}",
            graphId, isValid, errors.Count, warnings.Count);

        return global::System.Threading.Tasks.Task.FromResult(
            new WorkflowGraphValidationResult(graphId, isValid, errors, warnings, DateTimeOffset.UtcNow));
    }

    /// <inheritdoc />
    public global::System.Threading.Tasks.Task<WorkflowSimulationResult> SimulateWorkflowGraphAsync(
        Guid graphId, CancellationToken ct = default)
    {
        if (!_graphs.TryGetValue(graphId, out var graph))
            throw new InvalidOperationException($"Workflow graph {graphId} not found");

        var steps = new List<WorkflowSimulationStep>();
        var visited = new HashSet<Guid>();
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);
        var outEdges = graph.Edges
            .GroupBy(e => e.SourceNodeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Find entry nodes (no incoming edges)
        var targetIds = graph.Edges.Select(e => e.TargetNodeId).ToHashSet();
        var entryNodes = graph.Nodes.Where(n => !targetIds.Contains(n.Id)).ToList();

        if (entryNodes.Count == 0 && graph.Nodes.Count > 0)
            entryNodes = new List<WorkflowNode> { graph.Nodes[0] };

        int order = 1;
        var queue = new Queue<Guid>();
        foreach (var entry in entryNodes)
            queue.Enqueue(entry.Id);

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (!visited.Add(nodeId))
                continue;

            if (!nodeMap.TryGetValue(nodeId, out var node))
                continue;

            double simulatedDuration = node.NodeType switch
            {
                WorkflowNodeType.AgentNode => 500.0,
                WorkflowNodeType.ToolNode => 200.0,
                WorkflowNodeType.DecisionNode => 50.0,
                WorkflowNodeType.ConditionNode => 10.0,
                _ => 100.0
            };

            string outcome = node.NodeType switch
            {
                WorkflowNodeType.ConditionNode => "condition_true",
                WorkflowNodeType.DecisionNode => "branch_selected",
                _ => "completed"
            };

            steps.Add(new WorkflowSimulationStep(
                node.Id, node.Name, node.NodeType, order++, outcome, simulatedDuration));

            if (outEdges.TryGetValue(nodeId, out var edges))
            {
                foreach (var edge in edges)
                    queue.Enqueue(edge.TargetNodeId);
            }
        }

        var warnings = new List<string>();
        if (visited.Count < graph.Nodes.Count)
            warnings.Add($"{graph.Nodes.Count - visited.Count} node(s) are unreachable from entry points");

        double totalDuration = steps.Sum(s => s.DurationMs);

        _logger.LogInformation(
            "Workflow graph {GraphId} simulated: {StepCount} steps, {TotalMs}ms total",
            graphId, steps.Count, totalDuration);

        return global::System.Threading.Tasks.Task.FromResult(
            new WorkflowSimulationResult(graphId, true, steps, totalDuration, warnings, DateTimeOffset.UtcNow));
    }

    /// <inheritdoc />
    public global::System.Threading.Tasks.Task<WorkflowExportDefinition> ExportWorkflowGraphAsync(
        Guid graphId, string version = "1.0", CancellationToken ct = default)
    {
        if (!_graphs.TryGetValue(graphId, out var graph))
            throw new InvalidOperationException($"Workflow graph {graphId} not found");

        var export = new WorkflowExportDefinition(
            graph.Id,
            graph.Name,
            graph.Description,
            version,
            graph.Nodes,
            graph.Edges,
            graph.Metadata,
            DateTimeOffset.UtcNow);

        _logger.LogInformation("Workflow graph {GraphId} exported as version {Version}", graphId, version);

        return global::System.Threading.Tasks.Task.FromResult(export);
    }

    /// <inheritdoc />
    public global::System.Threading.Tasks.Task<bool> DeleteWorkflowGraphAsync(
        Guid graphId, CancellationToken ct = default)
    {
        bool removed = _graphs.TryRemove(graphId, out _);
        return global::System.Threading.Tasks.Task.FromResult(removed);
    }

    /// <summary>
    /// Validates basic structural constraints: non-empty graph, named nodes, valid edge references.
    /// </summary>
    internal static void ValidateStructure(WorkflowGraph graph, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(graph.Name))
            errors.Add("Workflow graph name is required");

        if (graph.Nodes.Count == 0)
            errors.Add("Workflow graph must have at least one node");

        var nodeIds = graph.Nodes.Select(n => n.Id).ToHashSet();

        // Check for duplicate node ids
        if (nodeIds.Count != graph.Nodes.Count)
            errors.Add("Workflow graph contains duplicate node ids");

        foreach (var node in graph.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Name))
                errors.Add($"Node {node.Id} has no name");
        }

        foreach (var edge in graph.Edges)
        {
            if (!nodeIds.Contains(edge.SourceNodeId))
                errors.Add($"Edge {edge.Id} references unknown source node {edge.SourceNodeId}");

            if (!nodeIds.Contains(edge.TargetNodeId))
                errors.Add($"Edge {edge.Id} references unknown target node {edge.TargetNodeId}");

            if (edge.SourceNodeId == edge.TargetNodeId)
                errors.Add($"Edge {edge.Id} is a self-loop on node {edge.SourceNodeId}");
        }
    }

    /// <summary>
    /// Validates node compatibility: DecisionNode and ConditionNode must have outgoing edges,
    /// AgentNode requires an agentType configuration value.
    /// </summary>
    internal static void ValidateNodeCompatibility(WorkflowGraph graph, List<string> errors, List<string> warnings)
    {
        var outEdgeCounts = graph.Edges
            .GroupBy(e => e.SourceNodeId)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var node in graph.Nodes)
        {
            int outgoing = outEdgeCounts.GetValueOrDefault(node.Id);

            switch (node.NodeType)
            {
                case WorkflowNodeType.DecisionNode:
                    if (outgoing < 2)
                        errors.Add($"DecisionNode '{node.Name}' must have at least 2 outgoing edges, found {outgoing}");
                    break;

                case WorkflowNodeType.ConditionNode:
                    if (outgoing == 0)
                        errors.Add($"ConditionNode '{node.Name}' must have at least 1 outgoing edge");
                    break;

                case WorkflowNodeType.AgentNode:
                    if (!node.Configuration.ContainsKey("agentType"))
                        warnings.Add($"AgentNode '{node.Name}' has no 'agentType' configured");
                    break;
            }
        }
    }

    /// <summary>
    /// Detects cycles (invalid loops) in the workflow graph using depth-first search.
    /// </summary>
    internal static void DetectInvalidLoops(WorkflowGraph graph, List<string> errors)
    {
        var adjacency = new Dictionary<Guid, List<Guid>>();
        foreach (var node in graph.Nodes)
            adjacency[node.Id] = new List<Guid>();

        foreach (var edge in graph.Edges)
        {
            if (adjacency.ContainsKey(edge.SourceNodeId))
                adjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
        }

        var nodeNames = graph.Nodes.ToDictionary(n => n.Id, n => n.Name);
        var visited = new HashSet<Guid>();
        var inStack = new HashSet<Guid>();

        foreach (var nodeId in adjacency.Keys)
        {
            if (!visited.Contains(nodeId) && HasCycleDfs(nodeId, adjacency, visited, inStack))
            {
                var cycleNodes = inStack
                    .Where(id => nodeNames.ContainsKey(id))
                    .Select(id => nodeNames[id]);
                errors.Add($"Cycle detected involving nodes: {string.Join(", ", cycleNodes)}");
                break;
            }
        }
    }

    private static bool HasCycleDfs(
        Guid nodeId,
        Dictionary<Guid, List<Guid>> adjacency,
        HashSet<Guid> visited,
        HashSet<Guid> inStack)
    {
        visited.Add(nodeId);
        inStack.Add(nodeId);

        if (adjacency.TryGetValue(nodeId, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (!visited.Contains(neighbor))
                {
                    if (HasCycleDfs(neighbor, adjacency, visited, inStack))
                        return true;
                }
                else if (inStack.Contains(neighbor))
                {
                    return true;
                }
            }
        }

        inStack.Remove(nodeId);
        return false;
    }
}

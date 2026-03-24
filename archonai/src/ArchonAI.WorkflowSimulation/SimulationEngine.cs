using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.StrategyLibrary;
using ArchonAI.Core.Models.Workflow;
using ArchonAI.Core.Models.WorkflowSimulation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.WorkflowSimulation;

/// <summary>
/// Core simulation engine that computes predictions from a workflow graph,
/// optional historical calibration data, and optional strategy resource profiles.
/// </summary>
public sealed class SimulationEngine : IWorkflowSimulationEngine
{
    private readonly SimulationEngineOptions _options;
    private readonly ILogger<SimulationEngine> _logger;

    public SimulationEngine(
        IOptions<SimulationEngineOptions> options,
        ILogger<SimulationEngine> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    // ── Full simulation ──────────────────────────────────────────────

    public global::System.Threading.Tasks.Task<WorkflowSimulationRunResult> RunSimulationAsync(
        WorkflowGraph graph,
        WorkflowHistoricalData? historicalData,
        StrategyTemplate? strategy,
        IReadOnlyDictionary<string, string>? overrides,
        CancellationToken ct = default)
    {
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);
        var outEdges = graph.Edges
            .GroupBy(e => e.SourceNodeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // BFS traversal from entry nodes
        var entryNodes = FindEntryNodes(graph);
        var visited = new HashSet<Guid>();
        var steps = new List<SimulatedStep>();
        var queue = new Queue<Guid>();
        int order = 1;

        foreach (var entry in entryNodes)
            queue.Enqueue(entry.Id);

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (!visited.Add(nodeId)) continue;
            if (!nodeMap.TryGetValue(nodeId, out var node)) continue;

            double baseDuration = GetBaseNodeDuration(node.NodeType);
            double successProb = GetBaseSuccessProbability(node.NodeType);

            // Calibrate from historical data
            if (historicalData is not null && historicalData.TotalExecutions > 0)
            {
                double calibrationWeight = Math.Min(1.0,
                    historicalData.TotalExecutions / (double)_options.CalibrationThreshold);
                successProb = (successProb * (1 - calibrationWeight))
                    + (historicalData.HistoricalSuccessRate * calibrationWeight);
                double latencyScale = historicalData.HistoricalAverageLatencyMs
                    / GetTotalBaselineDuration(graph);
                if (latencyScale > 0 && double.IsFinite(latencyScale))
                    baseDuration *= latencyScale;
            }

            // Apply overrides
            if (overrides is not null
                && overrides.TryGetValue($"node:{node.Name}:durationMs", out var durStr)
                && double.TryParse(durStr, out double durOverride))
            {
                baseDuration = durOverride;
            }

            string outcome = PredictOutcome(node.NodeType, successProb);

            steps.Add(new SimulatedStep(
                NodeId: node.Id,
                NodeName: node.Name,
                NodeType: node.NodeType.ToString(),
                Order: order++,
                PredictedOutcome: outcome,
                PredictedDurationMs: Math.Round(baseDuration, 2),
                SuccessProbability: Math.Round(successProb, 4)));

            if (outEdges.TryGetValue(nodeId, out var edges))
                foreach (var edge in edges)
                    queue.Enqueue(edge.TargetNodeId);
        }

        var warnings = new List<string>();
        if (visited.Count < graph.Nodes.Count)
            warnings.Add($"{graph.Nodes.Count - visited.Count} node(s) are unreachable");

        double totalLatency = steps.Sum(s => s.PredictedDurationMs);
        double overallSuccess = steps.Count > 0
            ? steps.Aggregate(1.0, (acc, s) => acc * s.SuccessProbability)
            : 0;
        double estimatedCost = EstimateCost(steps, strategy);

        var resourceEstimate = ComputeResourceEstimate(graph, strategy);

        var risks = new List<string>();
        if (overallSuccess < _options.RiskThreshold)
            risks.Add($"Overall success probability ({overallSuccess:P1}) below threshold ({_options.RiskThreshold:P1})");

        var lowProbNodes = steps.Where(s => s.SuccessProbability < 0.7).ToList();
        foreach (var lp in lowProbNodes)
            risks.Add($"Node '{lp.NodeName}' has low success probability ({lp.SuccessProbability:P1})");

        _logger.LogInformation(
            "Simulation completed for graph {GraphId}: {StepCount} steps, " +
            "latency={LatencyMs}ms, success={SuccessProb:P1}",
            graph.Id, steps.Count, totalLatency, overallSuccess);

        var result = new WorkflowSimulationRunResult(
            SimulationId: Guid.NewGuid(),
            WorkflowGraphId: graph.Id,
            StrategyId: strategy?.Id,
            PredictedSuccess: overallSuccess >= _options.RiskThreshold,
            PredictedSuccessProbability: Math.Round(overallSuccess, 4),
            EstimatedLatencyMs: Math.Round(totalLatency, 2),
            EstimatedCost: Math.Round(estimatedCost, 4),
            ResourceEstimate: resourceEstimate,
            Steps: steps,
            Risks: risks,
            Warnings: warnings,
            SimulatedAtUtc: DateTimeOffset.UtcNow);

        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    // ── Outcome prediction ───────────────────────────────────────────

    public global::System.Threading.Tasks.Task<WorkflowOutcomePrediction> PredictNodeOutcomesAsync(
        WorkflowGraph graph,
        WorkflowHistoricalData? historicalData,
        CancellationToken ct = default)
    {
        var predictions = new List<NodeOutcomePrediction>();
        double overallSuccess = 1.0;

        foreach (var node in graph.Nodes)
        {
            double successProb = GetBaseSuccessProbability(node.NodeType);
            double duration = GetBaseNodeDuration(node.NodeType);

            if (historicalData is not null && historicalData.TotalExecutions > 0)
            {
                double w = Math.Min(1.0,
                    historicalData.TotalExecutions / (double)_options.CalibrationThreshold);
                successProb = (successProb * (1 - w))
                    + (historicalData.HistoricalSuccessRate * w);
            }

            overallSuccess *= successProb;

            predictions.Add(new NodeOutcomePrediction(
                NodeId: node.Id,
                NodeName: node.Name,
                SuccessProbability: Math.Round(successProb, 4),
                EstimatedDurationMs: Math.Round(duration, 2),
                MostLikelyOutcome: PredictOutcome(node.NodeType, successProb)));
        }

        var criticalPath = FindCriticalPathNodes(graph);

        return global::System.Threading.Tasks.Task.FromResult(
            new WorkflowOutcomePrediction(
                SimulationId: Guid.NewGuid(),
                WorkflowGraphId: graph.Id,
                OverallSuccessProbability: Math.Round(overallSuccess, 4),
                OverallFailureProbability: Math.Round(1.0 - overallSuccess, 4),
                NodePredictions: predictions,
                CriticalPathNodes: criticalPath,
                PredictedAtUtc: DateTimeOffset.UtcNow));
    }

    // ── Resource estimation ──────────────────────────────────────────

    public global::System.Threading.Tasks.Task<WorkflowResourceEstimate> EstimateResourceUsageAsync(
        WorkflowGraph graph,
        StrategyTemplate? strategy,
        CancellationToken ct = default)
    {
        var estimate = ComputeResourceEstimate(graph, strategy);
        return global::System.Threading.Tasks.Task.FromResult(estimate);
    }

    // ── Latency estimation ───────────────────────────────────────────

    public global::System.Threading.Tasks.Task<WorkflowLatencyEstimate> EstimateLatencyAsync(
        WorkflowGraph graph,
        WorkflowHistoricalData? historicalData,
        CancellationToken ct = default)
    {
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);
        var criticalPathIds = FindCriticalPathNodeIds(graph);

        double totalMs = 0;
        double criticalPathMs = 0;
        var nodeEstimates = new List<NodeLatencyEstimate>();

        foreach (var node in graph.Nodes)
        {
            double baseMs = GetBaseNodeDuration(node.NodeType);
            double p95 = baseMs * _options.P95Multiplier;

            if (historicalData is not null && historicalData.TotalExecutions > 0)
            {
                double w = Math.Min(1.0,
                    historicalData.TotalExecutions / (double)_options.CalibrationThreshold);
                double scale = historicalData.HistoricalAverageLatencyMs
                    / GetTotalBaselineDuration(graph);
                if (scale > 0 && double.IsFinite(scale))
                {
                    baseMs *= ((1 - w) + (w * scale));
                    double p95Scale = historicalData.HistoricalP95LatencyMs
                        / GetTotalBaselineDuration(graph);
                    if (p95Scale > 0 && double.IsFinite(p95Scale))
                        p95 = GetBaseNodeDuration(node.NodeType) * p95Scale;
                }
            }

            bool isOnCriticalPath = criticalPathIds.Contains(node.Id);

            totalMs += baseMs;
            if (isOnCriticalPath)
                criticalPathMs += baseMs;

            nodeEstimates.Add(new NodeLatencyEstimate(
                NodeId: node.Id,
                NodeName: node.Name,
                EstimatedMs: Math.Round(baseMs, 2),
                P95EstimatedMs: Math.Round(p95, 2),
                IsOnCriticalPath: isOnCriticalPath));
        }

        return global::System.Threading.Tasks.Task.FromResult(
            new WorkflowLatencyEstimate(
                SimulationId: Guid.NewGuid(),
                WorkflowGraphId: graph.Id,
                TotalEstimatedMs: Math.Round(totalMs, 2),
                CriticalPathMs: Math.Round(criticalPathMs, 2),
                P50EstimatedMs: Math.Round(totalMs * _options.P50Multiplier, 2),
                P95EstimatedMs: Math.Round(totalMs * _options.P95Multiplier, 2),
                NodeEstimates: nodeEstimates,
                EstimatedAtUtc: DateTimeOffset.UtcNow));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static IReadOnlyList<WorkflowNode> FindEntryNodes(WorkflowGraph graph)
    {
        var targetIds = graph.Edges.Select(e => e.TargetNodeId).ToHashSet();
        var entries = graph.Nodes.Where(n => !targetIds.Contains(n.Id)).ToList();
        if (entries.Count == 0 && graph.Nodes.Count > 0)
            entries = new List<WorkflowNode> { graph.Nodes[0] };
        return entries;
    }

    private double GetBaseNodeDuration(WorkflowNodeType nodeType) => nodeType switch
    {
        WorkflowNodeType.AgentNode => _options.AgentNodeDurationMs,
        WorkflowNodeType.ToolNode => _options.ToolNodeDurationMs,
        WorkflowNodeType.DecisionNode => _options.DecisionNodeDurationMs,
        WorkflowNodeType.ConditionNode => _options.ConditionNodeDurationMs,
        _ => 100.0
    };

    private double GetBaseSuccessProbability(WorkflowNodeType nodeType) => nodeType switch
    {
        WorkflowNodeType.AgentNode => _options.AgentNodeSuccessProb,
        WorkflowNodeType.ToolNode => _options.ToolNodeSuccessProb,
        WorkflowNodeType.DecisionNode => _options.DecisionNodeSuccessProb,
        WorkflowNodeType.ConditionNode => _options.ConditionNodeSuccessProb,
        _ => _options.BaseSuccessProbability
    };

    private double GetTotalBaselineDuration(WorkflowGraph graph)
    {
        double total = graph.Nodes.Sum(n => GetBaseNodeDuration(n.NodeType));
        return total > 0 ? total : 1.0;
    }

    private static string PredictOutcome(WorkflowNodeType nodeType, double successProb)
    {
        if (successProb < 0.5) return "likely_failure";
        return nodeType switch
        {
            WorkflowNodeType.ConditionNode => "condition_evaluated",
            WorkflowNodeType.DecisionNode => "branch_selected",
            _ => "completed"
        };
    }

    private double EstimateCost(IReadOnlyList<SimulatedStep> steps, StrategyTemplate? strategy)
    {
        if (strategy is not null)
            return strategy.ResourceUsage.EstimatedCostPerExecution;

        return steps.Sum(s => s.PredictedDurationMs / 1000.0 * _options.CostPerSecond);
    }

    private WorkflowResourceEstimate ComputeResourceEstimate(
        WorkflowGraph graph, StrategyTemplate? strategy)
    {
        if (strategy is not null)
        {
            return new WorkflowResourceEstimate(
                EstimatedCpuSeconds: strategy.ResourceUsage.EstimatedCpuSeconds,
                EstimatedMemoryMb: strategy.ResourceUsage.EstimatedMemoryMb,
                EstimatedAgentCount: strategy.ResourceUsage.EstimatedAgentCount,
                EstimatedTotalCost: strategy.ResourceUsage.EstimatedCostPerExecution,
                CostCurrency: strategy.ResourceUsage.CostCurrency);
        }

        int agentCount = graph.Nodes.Count(n => n.NodeType == WorkflowNodeType.AgentNode);
        double totalDurationMs = graph.Nodes.Sum(n => GetBaseNodeDuration(n.NodeType));

        return new WorkflowResourceEstimate(
            EstimatedCpuSeconds: Math.Round(totalDurationMs / 1000.0 * _options.CpuSecondsPerSecond, 2),
            EstimatedMemoryMb: Math.Round(agentCount * _options.MemoryMbPerAgent, 2),
            EstimatedAgentCount: Math.Max(1, agentCount),
            EstimatedTotalCost: Math.Round(totalDurationMs / 1000.0 * _options.CostPerSecond, 4),
            CostCurrency: "USD");
    }

    private IReadOnlyList<string> FindCriticalPathNodes(WorkflowGraph graph)
    {
        return FindCriticalPathNodeIds(graph)
            .Select(id => graph.Nodes.First(n => n.Id == id).Name)
            .ToList();
    }

    /// <summary>
    /// Finds the longest path (critical path) through the DAG using topological ordering.
    /// </summary>
    private HashSet<Guid> FindCriticalPathNodeIds(WorkflowGraph graph)
    {
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);
        var outEdges = graph.Edges
            .GroupBy(e => e.SourceNodeId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.TargetNodeId).ToList());
        var inEdges = graph.Edges
            .GroupBy(e => e.TargetNodeId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.SourceNodeId).ToList());

        // Compute longest distance from any entry node
        var dist = new Dictionary<Guid, double>();
        var predecessor = new Dictionary<Guid, Guid?>();
        foreach (var node in graph.Nodes)
        {
            dist[node.Id] = 0;
            predecessor[node.Id] = null;
        }

        // Topological sort (Kahn's algorithm)
        var inDegree = graph.Nodes.ToDictionary(
            n => n.Id,
            n => inEdges.TryGetValue(n.Id, out var ins) ? ins.Count : 0);
        var sorted = new List<Guid>();
        var queue = new Queue<Guid>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            sorted.Add(id);
            if (!outEdges.TryGetValue(id, out var neighbours)) continue;
            foreach (var n in neighbours)
            {
                inDegree[n]--;
                if (inDegree[n] == 0) queue.Enqueue(n);
            }
        }

        // Longest path relaxation
        foreach (var id in sorted)
        {
            if (!outEdges.TryGetValue(id, out var neighbours)) continue;
            double currentDist = dist[id] + GetBaseNodeDuration(nodeMap[id].NodeType);
            foreach (var n in neighbours)
            {
                if (currentDist > dist[n])
                {
                    dist[n] = currentDist;
                    predecessor[n] = id;
                }
            }
        }

        // Trace back from the node with longest distance
        var criticalIds = new HashSet<Guid>();
        if (dist.Count == 0) return criticalIds;

        var endNode = dist.MaxBy(kv => kv.Value + GetBaseNodeDuration(nodeMap[kv.Key].NodeType)).Key;
        Guid? current = endNode;
        while (current.HasValue)
        {
            criticalIds.Add(current.Value);
            current = predecessor[current.Value];
        }

        return criticalIds;
    }
}

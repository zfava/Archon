using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Simulation;
using ArchonAI.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.WorkflowSimulation;

/// <summary>
/// Simulates strategies against TaskGraphs before execution. Computes per-node
/// success probabilities, durations, costs, and risk scores using agent type
/// baselines, strategy modifiers, and live registry performance data. The planner
/// uses comparative results to choose the final plan.
/// </summary>
public sealed class StrategySimulator : IStrategySimulator
{
    private readonly ITaskGraphBuilder _graphBuilder;
    private readonly IAgentCapabilityRegistry _capabilityRegistry;
    private readonly IEventBus _eventBus;
    private readonly StrategySimulatorOptions _options;
    private readonly ILogger<StrategySimulator> _logger;

    public StrategySimulator(
        ITaskGraphBuilder graphBuilder,
        IAgentCapabilityRegistry capabilityRegistry,
        IEventBus eventBus,
        IOptions<StrategySimulatorOptions> options,
        ILogger<StrategySimulator> logger)
    {
        _graphBuilder = graphBuilder;
        _capabilityRegistry = capabilityRegistry;
        _eventBus = eventBus;
        _options = options.Value;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  Single strategy simulation
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<StrategySimulationResult> SimulateAsync(
        TaskGraph graph,
        string strategy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var layers = graph.GetExecutionLayers();
        var criticalPathNodeIds = ComputeCriticalPath(graph);

        var nodeResults = new List<SimulatedNodeResult>();
        double overallSuccess = 1.0;
        double totalDuration = 0;
        decimal totalCost = 0;
        var risks = new List<string>();
        var warnings = new List<string>();

        foreach (var layer in layers)
        {
            double layerMaxDuration = 0;

            foreach (var node in layer)
            {
                double baseSuccessProb = GetBaseSuccessProb(node.AgentType);
                double baseDuration = node.EstimatedDurationHours;
                decimal baseCost = GetCostPerHour(node.AgentType) * (decimal)baseDuration;

                // Apply strategy modifiers
                (baseSuccessProb, baseDuration, baseCost) =
                    ApplyStrategyModifiers(strategy, baseSuccessProb, baseDuration, baseCost);

                // Calibrate from live registry data
                (baseSuccessProb, baseDuration) =
                    await CalibrateFromRegistryAsync(node.AgentType, baseSuccessProb, baseDuration, cancellationToken);

                // Dependency risk: each required dependency that could fail adds compound risk
                var deps = graph.GetDependencies(node.NodeId);
                int requiredDeps = graph.Edges
                    .Count(e => e.TargetNodeId == node.NodeId && e.IsRequired);
                if (requiredDeps > 2)
                {
                    double depRiskPenalty = requiredDeps * 0.01;
                    baseSuccessProb = Math.Max(0.1, baseSuccessProb - depRiskPenalty);
                }

                bool isOnCriticalPath = criticalPathNodeIds.Contains(node.NodeId);
                var nodeRisks = new List<string>();

                if (baseSuccessProb < _options.NodeRiskThreshold)
                    nodeRisks.Add($"Low success probability ({baseSuccessProb:P1})");
                if (baseDuration > 4.0)
                    nodeRisks.Add($"Long estimated duration ({baseDuration:F1}h)");
                if (isOnCriticalPath && baseSuccessProb < 0.9)
                    nodeRisks.Add("Critical path node with sub-90% success probability");

                overallSuccess *= baseSuccessProb;
                layerMaxDuration = Math.Max(layerMaxDuration, baseDuration);
                totalCost += baseCost;

                nodeResults.Add(new SimulatedNodeResult(
                    NodeId: node.NodeId,
                    NodeName: node.Name,
                    AgentType: node.AgentType,
                    SuccessProbability: Math.Round(baseSuccessProb, 4),
                    EstimatedDurationHours: Math.Round(baseDuration, 2),
                    EstimatedCost: Math.Round(baseCost, 4),
                    IsOnCriticalPath: isOnCriticalPath,
                    NodeRisks: nodeRisks));
            }

            // Parallel layers: duration = max of nodes in layer
            totalDuration += layerMaxDuration;
        }

        // Risk score: 1 - overall success probability
        double riskScore = Math.Round(1.0 - overallSuccess, 4);
        string riskLevel = riskScore >= _options.HighRiskThreshold ? "high"
            : riskScore >= _options.MediumRiskThreshold ? "medium"
            : "low";

        // Aggregate risks
        if (overallSuccess < _options.MinAcceptableSuccessProb)
            risks.Add($"Overall success probability ({overallSuccess:P1}) is below minimum threshold ({_options.MinAcceptableSuccessProb:P1})");

        var lowProbNodes = nodeResults.Where(n => n.SuccessProbability < _options.NodeRiskThreshold).ToList();
        foreach (var lpn in lowProbNodes)
            risks.Add($"Node '{lpn.NodeName}' ({lpn.AgentType}) has low success probability ({lpn.SuccessProbability:P1})");

        var criticalPathRiskyNodes = nodeResults
            .Where(n => n.IsOnCriticalPath && n.SuccessProbability < 0.9)
            .ToList();
        if (criticalPathRiskyNodes.Count > 0)
            risks.Add($"{criticalPathRiskyNodes.Count} critical-path node(s) have sub-90% success probability");

        if (layers.Count > 0 && graph.Nodes.Count > layers.Count * 3)
            warnings.Add("High node-to-layer ratio may indicate sequential bottlenecks");

        int parallelismDegree = layers.Count > 0 ? layers.Max(l => l.Count) : 0;

        string outcomeLabel = overallSuccess switch
        {
            >= 0.9 => "highly_likely_success",
            >= 0.7 => "likely_success",
            >= 0.5 => "uncertain",
            _ => "likely_failure"
        };

        var expectedOutcome = new ExpectedOutcome(
            OverallSuccessProbability: Math.Round(overallSuccess, 4),
            PredictedSuccess: overallSuccess >= _options.MinAcceptableSuccessProb,
            PredictedOutcomeLabel: outcomeLabel,
            Confidence: Math.Round(ComputeConfidence(nodeResults, layers.Count), 4),
            TotalNodes: graph.Nodes.Count,
            CriticalPathLength: criticalPathNodeIds.Count,
            ParallelismDegree: parallelismDegree);

        var result = new StrategySimulationResult(
            SimulationId: Guid.NewGuid(),
            GraphId: graph.GraphId,
            Strategy: strategy,
            ExpectedOutcome: expectedOutcome,
            RiskScore: riskScore,
            RiskLevel: riskLevel,
            NodeResults: nodeResults,
            Risks: risks,
            Warnings: warnings,
            EstimatedTotalDurationHours: Math.Round(totalDuration, 2),
            EstimatedTotalCost: Math.Round(totalCost, 4),
            SimulatedAtUtc: DateTimeOffset.UtcNow);

        _logger.LogInformation(
            "Strategy simulation '{Strategy}' for graph {GraphId}: success={SuccessProb:P1}, risk={RiskScore:F3} ({RiskLevel}), duration={Duration:F1}h, cost={Cost:F4}",
            strategy, graph.GraphId, overallSuccess, riskScore, riskLevel, totalDuration, totalCost);

        return result;
    }

    // ══════════════════════════════════════════════════════════════
    //  Multi-strategy comparison
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<TaskGraphStrategyComparison> CompareStrategiesAsync(
        TaskGraph graph,
        IReadOnlyList<string> strategies,
        CancellationToken cancellationToken = default)
    {
        var simulations = new List<StrategySimulationResult>();

        foreach (var strategy in strategies.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var sim = await SimulateAsync(graph, strategy, cancellationToken);
            simulations.Add(sim);
        }

        var (recommended, reason) = SelectBestSimulation(simulations);

        await _eventBus.PublishAsync(new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: "simulation.strategy.comparison.completed",
            Source: nameof(StrategySimulator),
            CorrelationId: graph.GoalId,
            Payload: new Dictionary<string, string>
            {
                ["graphId"] = graph.GraphId.ToString(),
                ["goalId"] = graph.GoalId.ToString(),
                ["strategiesCompared"] = strategies.Count.ToString(),
                ["recommendedStrategy"] = recommended.Strategy,
                ["recommendedRiskScore"] = recommended.RiskScore.ToString("F4"),
                ["recommendedSuccessProb"] = recommended.ExpectedOutcome.OverallSuccessProbability.ToString("F4")
            },
            OccurredAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        _logger.LogInformation(
            "Strategy comparison for graph {GraphId}: recommended '{Strategy}' (success={SuccessProb:P1}, risk={Risk:F3}). Reason: {Reason}",
            graph.GraphId, recommended.Strategy, recommended.ExpectedOutcome.OverallSuccessProbability,
            recommended.RiskScore, reason);

        return new TaskGraphStrategyComparison(
            GoalId: graph.GoalId,
            Simulations: simulations,
            RecommendedSimulation: recommended,
            RecommendationReason: reason,
            ComparedAtUtc: DateTimeOffset.UtcNow);
    }

    // ══════════════════════════════════════════════════════════════
    //  Goal-level: build graph per strategy, simulate, compare
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<TaskGraphStrategyComparison> SimulateGoalStrategiesAsync(
        OperationalGoal goal,
        IReadOnlyList<string>? candidateStrategies,
        CancellationToken cancellationToken = default)
    {
        var strategies = candidateStrategies is { Count: > 0 }
            ? candidateStrategies
            : (IReadOnlyList<string>)new[] { "balanced", "safe-mode", "throughput-optimized", "cost-optimized" };

        var simulations = new List<StrategySimulationResult>();

        foreach (var strategy in strategies.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var graph = await _graphBuilder.BuildGraphAsync(goal, strategy, cancellationToken);
            var sim = await SimulateAsync(graph, strategy, cancellationToken);
            simulations.Add(sim);
        }

        var (recommended, reason) = SelectBestSimulation(simulations);

        await _eventBus.PublishAsync(new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: "simulation.goal.strategies.compared",
            Source: nameof(StrategySimulator),
            CorrelationId: goal.GoalId,
            Payload: new Dictionary<string, string>
            {
                ["goalId"] = goal.GoalId.ToString(),
                ["goalTitle"] = goal.Title,
                ["strategiesCompared"] = strategies.Count.ToString(),
                ["recommendedStrategy"] = recommended.Strategy,
                ["recommendedRiskScore"] = recommended.RiskScore.ToString("F4")
            },
            OccurredAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        return new TaskGraphStrategyComparison(
            GoalId: goal.GoalId,
            Simulations: simulations,
            RecommendedSimulation: recommended,
            RecommendationReason: reason,
            ComparedAtUtc: DateTimeOffset.UtcNow);
    }

    // ══════════════════════════════════════════════════════════════
    //  Internals
    // ══════════════════════════════════════════════════════════════

    private double GetBaseSuccessProb(string agentType)
    {
        return agentType.ToLowerInvariant() switch
        {
            "context-analysis" => _options.ContextAnalysisSuccessProb,
            "workflow-orchestration" => _options.WorkflowOrchestrationSuccessProb,
            "operation-execution" => _options.OperationExecutionSuccessProb,
            "outcome-validation" => _options.OutcomeValidationSuccessProb,
            "financial-operations" => _options.FinancialOperationsSuccessProb,
            _ => _options.DefaultSuccessProb
        };
    }

    private decimal GetCostPerHour(string agentType)
    {
        return agentType.ToLowerInvariant() switch
        {
            "context-analysis" => _options.ContextAnalysisCostPerHour,
            "workflow-orchestration" => _options.WorkflowOrchestrationCostPerHour,
            "operation-execution" => _options.OperationExecutionCostPerHour,
            "outcome-validation" => _options.OutcomeValidationCostPerHour,
            _ => _options.DefaultCostPerHour
        };
    }

    private (double successProb, double duration, decimal cost) ApplyStrategyModifiers(
        string strategy, double successProb, double duration, decimal cost)
    {
        return strategy.ToLowerInvariant() switch
        {
            "safe-mode" => (
                Math.Min(1.0, successProb + _options.SafeModeSuccessBoost),
                duration * _options.SafeModeDurationMultiplier,
                cost * (decimal)_options.SafeModeDurationMultiplier),

            "throughput-optimized" => (
                Math.Max(0.1, successProb - _options.ThroughputSuccessPenalty),
                duration * _options.ThroughputDurationMultiplier,
                cost * (decimal)_options.ThroughputDurationMultiplier),

            "cost-optimized" => (
                Math.Max(0.1, successProb - _options.CostOptimizedSuccessPenalty),
                duration,
                cost * (decimal)_options.CostOptimizedCostMultiplier),

            _ => (successProb, duration, cost) // balanced — no modifiers
        };
    }

    private async global::System.Threading.Tasks.Task<(double successProb, double duration)> CalibrateFromRegistryAsync(
        string agentType,
        double baseSuccessProb,
        double baseDuration,
        CancellationToken cancellationToken)
    {
        var agents = await _capabilityRegistry.QueryByCapabilityAsync(agentType, cancellationToken);
        if (agents.Count == 0) return (baseSuccessProb, baseDuration);

        // Use best-performing agent's metrics for calibration
        var best = agents
            .Where(a => a.Executions > 0)
            .OrderByDescending(a => a.SuccessRate)
            .FirstOrDefault();

        if (best is null) return (baseSuccessProb, baseDuration);

        // Blend base estimates with real data (weight increases with execution count)
        double calibrationWeight = Math.Min(0.8, best.Executions / 100.0);
        double calibratedSuccess = (baseSuccessProb * (1 - calibrationWeight))
            + (best.SuccessRate * calibrationWeight);

        // Convert latency to hours for duration calibration
        double registryDurationHours = best.AverageLatencyMs / 3_600_000.0;
        if (registryDurationHours > 0 && double.IsFinite(registryDurationHours))
        {
            double durationScale = registryDurationHours / baseDuration;
            if (durationScale > 0.1 && durationScale < 10.0)
                baseDuration *= ((1 - calibrationWeight) + (calibrationWeight * durationScale));
        }

        return (calibratedSuccess, baseDuration);
    }

    private static HashSet<Guid> ComputeCriticalPath(TaskGraph graph)
    {
        var nodeMap = graph.Nodes.ToDictionary(n => n.NodeId);
        var outEdges = graph.Edges
            .Where(e => e.IsRequired)
            .GroupBy(e => e.SourceNodeId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.TargetNodeId).ToList());
        var inEdges = graph.Edges
            .Where(e => e.IsRequired)
            .GroupBy(e => e.TargetNodeId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.SourceNodeId).ToList());

        // Topological sort (Kahn's algorithm)
        var inDegree = graph.Nodes.ToDictionary(
            n => n.NodeId,
            n => inEdges.TryGetValue(n.NodeId, out var ins) ? ins.Count : 0);
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

        // Longest-path relaxation
        var dist = graph.Nodes.ToDictionary(n => n.NodeId, _ => 0.0);
        var predecessor = graph.Nodes.ToDictionary(n => n.NodeId, _ => (Guid?)null);

        foreach (var id in sorted)
        {
            if (!outEdges.TryGetValue(id, out var neighbours)) continue;
            double currentDist = dist[id] + nodeMap[id].EstimatedDurationHours;
            foreach (var n in neighbours)
            {
                if (currentDist > dist[n])
                {
                    dist[n] = currentDist;
                    predecessor[n] = id;
                }
            }
        }

        // Trace back from the node with the longest distance
        var criticalIds = new HashSet<Guid>();
        if (dist.Count == 0) return criticalIds;

        var endNode = dist.MaxBy(kv => kv.Value + nodeMap[kv.Key].EstimatedDurationHours).Key;
        Guid? current = endNode;
        while (current.HasValue)
        {
            criticalIds.Add(current.Value);
            current = predecessor[current.Value];
        }

        return criticalIds;
    }

    private static (StrategySimulationResult Recommended, string Reason) SelectBestSimulation(
        List<StrategySimulationResult> simulations)
    {
        if (simulations.Count == 1)
            return (simulations[0], "Only strategy evaluated");

        // Composite score: 50% success probability, 25% inverse risk, 15% inverse duration, 10% inverse cost
        var scored = simulations
            .Select(s =>
            {
                double successScore = s.ExpectedOutcome.OverallSuccessProbability;
                double riskScore = 1.0 - s.RiskScore;
                double durationScore = 1.0 / (1.0 + s.EstimatedTotalDurationHours);
                double costScore = 1.0 / (1.0 + (double)s.EstimatedTotalCost);

                double composite = (0.50 * successScore)
                                 + (0.25 * riskScore)
                                 + (0.15 * durationScore)
                                 + (0.10 * costScore);

                return (Simulation: s, Composite: composite);
            })
            .OrderByDescending(x => x.Composite)
            .ToList();

        var best = scored[0];
        var runner = scored.Count > 1 ? scored[1] : scored[0];

        string reason = $"Strategy '{best.Simulation.Strategy}' scored {best.Composite:F3} " +
                        $"(success={best.Simulation.ExpectedOutcome.OverallSuccessProbability:P1}, " +
                        $"risk={best.Simulation.RiskScore:F3}, " +
                        $"duration={best.Simulation.EstimatedTotalDurationHours:F1}h, " +
                        $"cost={best.Simulation.EstimatedTotalCost:F4})";

        if (scored.Count > 1)
            reason += $". Runner-up: '{runner.Simulation.Strategy}' ({runner.Composite:F3})";

        return (best.Simulation, reason);
    }

    private static double ComputeConfidence(IReadOnlyList<SimulatedNodeResult> nodeResults, int layerCount)
    {
        if (nodeResults.Count == 0) return 0;

        // Confidence is higher when:
        // - Nodes have high and consistent success probabilities
        // - The graph has good parallelism (more layers = more sequential risk)
        double avgSuccess = nodeResults.Average(n => n.SuccessProbability);
        double variance = nodeResults.Average(n => Math.Pow(n.SuccessProbability - avgSuccess, 2));
        double consistencyScore = 1.0 - Math.Min(1.0, variance * 10);
        double parallelismScore = layerCount > 0 ? Math.Min(1.0, (double)nodeResults.Count / layerCount / 5.0) : 0;

        return Math.Clamp((avgSuccess * 0.6) + (consistencyScore * 0.3) + (parallelismScore * 0.1), 0, 1);
    }
}

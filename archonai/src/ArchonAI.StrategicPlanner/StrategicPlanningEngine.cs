using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Reasoning;
using ArchonAI.Core.Models.Simulation;

namespace ArchonAI.StrategicPlanner;

/// <summary>
/// Builds optimized workflow definitions from objective intent and constraints.
/// Runs scenario simulations and economic evaluations before finalizing plans.
/// Uses strategy simulation to choose the best TaskGraph-based plan for goals.
/// </summary>
public sealed class StrategicPlanningEngine : IStrategicPlanner
{
    private readonly IScenarioEngine _scenarioEngine;
    private readonly IEconomicEvaluator _economicEvaluator;
    private readonly IStrategySimulator _strategySimulator;
    private readonly ITaskGraphBuilder _taskGraphBuilder;

    public StrategicPlanningEngine(
        IScenarioEngine scenarioEngine,
        IEconomicEvaluator economicEvaluator,
        IStrategySimulator strategySimulator,
        ITaskGraphBuilder taskGraphBuilder)
    {
        _scenarioEngine = scenarioEngine;
        _economicEvaluator = economicEvaluator;
        _strategySimulator = strategySimulator;
        _taskGraphBuilder = taskGraphBuilder;
    }

    public global::System.Threading.Tasks.Task<WorkflowDefinition> BuildWorkflowAsync(
        Objective objective,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedConstraints = objective.Constraints
            .ToDictionary(kvp => kvp.Key.Trim(), kvp => kvp.Value.Trim(), StringComparer.OrdinalIgnoreCase);

        string strategy = SelectStrategy(normalizedConstraints, objective);
        string executionAgentType = SelectExecutionAgentType(normalizedConstraints, strategy);

        IReadOnlyList<WorkflowStepDefinition> steps =
        [
            new WorkflowStepDefinition(
                Order: 10,
                Name: "Objective Analysis",
                Description: "Analyze business objective intent, constraints, and risk profile.",
                AgentType: "context-analysis",
                Inputs: new Dictionary<string, string>(normalizedConstraints)),
            new WorkflowStepDefinition(
                Order: 20,
                Name: "Operational Planning",
                Description: "Design operational tasks aligned to strategic workflow.",
                AgentType: "workflow-orchestration",
                Inputs: new Dictionary<string, string>
                {
                    ["strategy"] = strategy,
                    ["objectiveTitle"] = objective.Title
                }),
            new WorkflowStepDefinition(
                Order: 30,
                Name: "Execution",
                Description: objective.Description,
                AgentType: executionAgentType,
                Inputs: new Dictionary<string, string>
                {
                    ["strategy"] = strategy,
                    ["objectiveId"] = objective.Id.ToString()
                }),
            new WorkflowStepDefinition(
                Order: 40,
                Name: "Evaluation",
                Description: "Evaluate outcomes and capture optimization feedback for future runs.",
                AgentType: "outcome-validation",
                Inputs: new Dictionary<string, string>
                {
                    ["strategy"] = strategy,
                    ["feedbackMode"] = "adaptive"
                })
        ];

        var definition = new WorkflowDefinition(
            ObjectiveId: objective.Id,
            Strategy: strategy,
            Summary: $"Strategic workflow generated for '{objective.Title}' using '{strategy}' strategy.",
            Steps: steps,
            CreatedAtUtc: DateTimeOffset.UtcNow);

        return global::System.Threading.Tasks.Task.FromResult(definition);
    }

    public async global::System.Threading.Tasks.Task<SimulationValidatedPlan> BuildAndValidateWorkflowAsync(
        Objective objective,
        IReadOnlyList<string>? candidateStrategies = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WorkflowDefinition workflow = await BuildWorkflowAsync(objective, cancellationToken);

        var strategies = candidateStrategies is { Count: > 0 }
            ? candidateStrategies
            : (IReadOnlyList<string>)new[] { workflow.Strategy, "safe-mode", "balanced", "throughput-optimized", "cost-optimized" };

        // Economic evaluation ranks candidates by cost, impact, success probability, and time.
        // Reorder candidates so the economically best strategy is evaluated first by the scenario engine.
        EconomicEvaluationResult economicResult = await _economicEvaluator.EvaluateStrategiesAsync(
            objective, workflow, strategies, cancellationToken: cancellationToken);

        var rankedStrategies = economicResult.Evaluations
            .OrderByDescending(e => e.EconomicScore)
            .Select(e => e.Strategy)
            .ToList();

        // Update workflow strategy to the economically optimal choice before scenario validation
        workflow = workflow with
        {
            Strategy = economicResult.BestStrategy.Strategy,
            Summary = $"{workflow.Summary} Economic evaluation selected '{economicResult.BestStrategy.Strategy}' " +
                      $"(score {economicResult.BestStrategy.EconomicScore:F3})."
        };

        return await _scenarioEngine.ValidatePlanAsync(objective, workflow, rankedStrategies, cancellationToken);
    }

    public async global::System.Threading.Tasks.Task<SimulationGuidedPlan> BuildSimulationGuidedPlanAsync(
        OperationalGoal goal,
        IReadOnlyList<string>? candidateStrategies = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Simulate all candidate strategies against the goal's TaskGraph
        TaskGraphStrategyComparison comparison = await _strategySimulator.SimulateGoalStrategiesAsync(
            goal, candidateStrategies, cancellationToken);

        var bestSim = comparison.RecommendedSimulation;

        // Build the final TaskGraph using the winning strategy
        TaskGraph selectedGraph = await _taskGraphBuilder.BuildGraphAsync(
            goal, bestSim.Strategy, cancellationToken);

        return new SimulationGuidedPlan(
            Goal: goal,
            SelectedTaskGraph: selectedGraph,
            SelectedSimulation: bestSim,
            ComparisonResult: comparison,
            SelectedStrategy: bestSim.Strategy,
            ExpectedSuccessProbability: bestSim.ExpectedOutcome.OverallSuccessProbability,
            RiskScore: bestSim.RiskScore,
            PlanDecisionReason: comparison.RecommendationReason,
            PlannedAtUtc: DateTimeOffset.UtcNow);
    }

    private static string SelectStrategy(IReadOnlyDictionary<string, string> constraints, Objective objective)
    {
        bool deadlineSensitive = objective.DueAtUtc is not null && (objective.DueAtUtc.Value - DateTimeOffset.UtcNow).TotalHours <= 24;
        bool highRisk = constraints.TryGetValue("risk", out string? risk) && risk.Equals("high", StringComparison.OrdinalIgnoreCase);
        bool costSensitive = constraints.TryGetValue("cost", out string? cost) && cost.Equals("strict", StringComparison.OrdinalIgnoreCase);

        if (highRisk)
        {
            return "safe-mode";
        }

        if (deadlineSensitive)
        {
            return "throughput-optimized";
        }

        if (costSensitive)
        {
            return "cost-optimized";
        }

        return "balanced";
    }

    private static string SelectExecutionAgentType(IReadOnlyDictionary<string, string> constraints, string strategy)
    {
        if (strategy.Equals("safe-mode", StringComparison.OrdinalIgnoreCase))
        {
            return "workflow-orchestration";
        }

        if (constraints.TryGetValue("domain", out string? domain) &&
            domain.Equals("financial", StringComparison.OrdinalIgnoreCase))
        {
            return "financial-operations";
        }

        return "operation-execution";
    }
}

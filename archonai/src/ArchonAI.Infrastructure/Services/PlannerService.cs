using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Knowledge;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Simulation;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Infrastructure.Services;

/// <summary>
/// Operational planner that consumes strategic workflow definitions and emits executable tasks.
/// Runs strategy simulations before execution planning.
/// </summary>
public sealed class PlannerService : IPlanner
{
    private readonly IPlanningFeedbackStore _feedbackStore;
    private readonly IStrategicPlanner _strategicPlanner;
    private readonly IKnowledgeGraphStore _knowledgeGraphStore;
    private readonly ISimulationEngine _simulationEngine;
    private readonly IStrategyStore _strategyStore;

    public PlannerService(
        IPlanningFeedbackStore feedbackStore,
        IStrategicPlanner strategicPlanner,
        IKnowledgeGraphStore knowledgeGraphStore,
        ISimulationEngine simulationEngine,
        IStrategyStore strategyStore)
    {
        _feedbackStore = feedbackStore;
        _strategicPlanner = strategicPlanner;
        _knowledgeGraphStore = knowledgeGraphStore;
        _simulationEngine = simulationEngine;
        _strategyStore = strategyStore;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<CoreTask>> CreatePlanAsync(Objective objective, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PlanningFeedback> recentFeedback = await _feedbackStore.GetRecentAsync(25, cancellationToken);
        WorkflowDefinition strategicWorkflow = await _strategicPlanner.BuildWorkflowAsync(objective, cancellationToken);
        string objectiveType = objective.Constraints.GetValueOrDefault("objectiveType", "default");
        IReadOnlyList<OperationalStrategy> reusableStrategies = await _strategyStore.QueryByObjectiveTypeAsync(objectiveType, cancellationToken);
        OperationalStrategy? selectedStrategy = reusableStrategies.FirstOrDefault();

        string objectiveNodeId = $"objective:{objective.Id}";
        IReadOnlyList<KnowledgeNode> relatedSystems =
            await _knowledgeGraphStore.QueryRelatedNodesAsync(objectiveNodeId, "depends_on", cancellationToken);

        bool shouldUseSafeMode = recentFeedback
            .TakeLast(5)
            .Any(feedback => !feedback.WasSuccessful || feedback.Strategy.Equals("safe-mode", StringComparison.OrdinalIgnoreCase));

        var candidates = shouldUseSafeMode
            ? new[] { "safe-mode" }
            : new[]
            {
                selectedStrategy?.WorkflowTemplate ?? strategicWorkflow.Strategy,
                strategicWorkflow.Strategy,
                "balanced",
                "safe-mode",
                "throughput-optimized"
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        StrategyComparisonResult simulation = await _simulationEngine.CompareStrategiesAsync(
            objective,
            strategicWorkflow,
            candidates,
            cancellationToken);

        string strategy = simulation.RecommendedStrategy;
        string relatedSystemsCsv = string.Join(',', relatedSystems.Select(n => n.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase));

        var now = DateTimeOffset.UtcNow;
        var tasks = strategicWorkflow.Steps
            .Select((step, index) => new { step, index })
            .Select(step => new CoreTask(
                Guid.NewGuid(),
                objective.Id,
                step.step.Order,
                step.step.Name,
                step.step.Description,
                ResolveCapability(
                    ResolveRecommendedAgent(selectedStrategy, step.index, step.step.AgentType),
                    strategy),
                new Dictionary<string, string>(step.step.Inputs)
                {
                    ["strategy"] = strategy,
                    ["objectiveType"] = objectiveType,
                    ["strategySource"] = selectedStrategy is null ? "strategic-planner" : "reusable-strategy",
                    ["strategyTemplate"] = selectedStrategy?.WorkflowTemplate ?? strategicWorkflow.Strategy,
                    ["strategyRecommendedAgents"] = selectedStrategy is null ? string.Empty : string.Join(',', selectedStrategy.RecommendedAgents),
                    ["relatedSystems"] = relatedSystemsCsv,
                    ["simulationRecommendedStrategy"] = simulation.RecommendedStrategy,
                    ["simulationReason"] = simulation.Reason
                },
                now,
                null,
                null))
            .OrderBy(task => task.Order)
            .ToArray();

        return tasks;
    }

    private static string ResolveRecommendedAgent(OperationalStrategy? strategy, int index, string fallbackAgentType)
    {
        if (strategy is null || strategy.RecommendedAgents.Count <= index)
        {
            return fallbackAgentType;
        }

        string recommended = strategy.RecommendedAgents[index];
        return string.IsNullOrWhiteSpace(recommended) ? fallbackAgentType : recommended;
    }

    private static string ResolveCapability(string agentType, string strategy)
    {
        if (strategy.Equals("safe-mode", StringComparison.OrdinalIgnoreCase) &&
            agentType.Equals("operation-execution", StringComparison.OrdinalIgnoreCase))
        {
            return "workflow-orchestration";
        }

        return agentType;
    }
}

using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Knowledge;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Context;
using ArchonAI.Core.Models.Perception;
using ArchonAI.Core.Models.Simulation;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Infrastructure.Services;

/// <summary>
/// Operational planner that consumes strategic workflow definitions and emits executable tasks.
/// Runs scenario simulations before execution planning so that simulation results influence final decisions.
/// </summary>
public sealed class PlannerService : IPlanner
{
    private readonly IPlanningFeedbackStore _feedbackStore;
    private readonly IStrategicPlanner _strategicPlanner;
    private readonly IKnowledgeGraphStore _knowledgeGraphStore;
    private readonly ISimulationEngine _simulationEngine;
    private readonly IScenarioEngine _scenarioEngine;
    private readonly IStrategyStore _strategyStore;
    private readonly IContextEngine _contextEngine;
    private readonly IPerceptionEngine _perceptionEngine;
    private readonly IKnowledgeGraphEngine _knowledgeGraphEngine;
    private readonly IDataFabricEngine _dataFabricEngine;
    private readonly IMultiTenantContext _tenantContext;
    private readonly ITenantResourceGovernor _tenantResourceGovernor;
    private readonly IOptimizationEngine _optimizationEngine;

    public PlannerService(
        IPlanningFeedbackStore feedbackStore,
        IStrategicPlanner strategicPlanner,
        IKnowledgeGraphStore knowledgeGraphStore,
        ISimulationEngine simulationEngine,
        IScenarioEngine scenarioEngine,
        IStrategyStore strategyStore,
        IContextEngine contextEngine,
        IPerceptionEngine perceptionEngine,
        IKnowledgeGraphEngine knowledgeGraphEngine,
        IDataFabricEngine dataFabricEngine,
        IMultiTenantContext tenantContext,
        ITenantResourceGovernor tenantResourceGovernor,
        IOptimizationEngine optimizationEngine)
    {
        _feedbackStore = feedbackStore;
        _strategicPlanner = strategicPlanner;
        _knowledgeGraphStore = knowledgeGraphStore;
        _simulationEngine = simulationEngine;
        _scenarioEngine = scenarioEngine;
        _strategyStore = strategyStore;
        _contextEngine = contextEngine;
        _perceptionEngine = perceptionEngine;
        _knowledgeGraphEngine = knowledgeGraphEngine;
        _dataFabricEngine = dataFabricEngine;
        _tenantContext = tenantContext;
        _tenantResourceGovernor = tenantResourceGovernor;
        _optimizationEngine = optimizationEngine;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<CoreTask>> CreatePlanAsync(Objective objective, CancellationToken cancellationToken = default)
    {
        string tenantId = objective.Constraints.GetValueOrDefault("tenantId", "default-org");
        using IDisposable tenantScope = _tenantContext.BeginTenantScope(tenantId);

        bool slotAcquired = await _tenantResourceGovernor.TryAcquirePlanningSlotAsync(tenantId, cancellationToken);
        if (!slotAcquired)
        {
            throw new InvalidOperationException($"Tenant '{tenantId}' exceeded concurrent planning slots.");
        }

        try
        {
            PerceptionResult perceptionResult = await _perceptionEngine.ProcessObjectiveAsync(objective, cancellationToken);
        if (!perceptionResult.IsValid)
        {
            throw new InvalidOperationException($"Objective failed perception validation: {string.Join("; ", perceptionResult.ValidationErrors)}");
        }

        Objective perceivedObjective = perceptionResult.NormalizedObjective;
        IReadOnlyList<PlanningFeedback> recentFeedback = await _feedbackStore.GetRecentAsync(25, cancellationToken);
        WorkflowDefinition strategicWorkflow = await _strategicPlanner.BuildWorkflowAsync(perceivedObjective, cancellationToken);
        WorkflowDefinition optimizedWorkflow = await _optimizationEngine.OptimizeWorkflowAsync(perceivedObjective, strategicWorkflow, cancellationToken);
        IReadOnlyList<OperationalStrategy> improvedStrategies = await _optimizationEngine.GenerateImprovedStrategiesAsync(perceivedObjective, optimizedWorkflow, cancellationToken);
        foreach (OperationalStrategy improvedStrategy in improvedStrategies)
        {
            await _strategyStore.SaveAsync(improvedStrategy, cancellationToken);
        }

        await _optimizationEngine.DeployOptimizedWorkflowAsync(optimizedWorkflow, cancellationToken);
        SystemContext systemContext = await _contextEngine.GetContextAsync(cancellationToken);
        string objectiveType = perceivedObjective.Constraints.GetValueOrDefault("objectiveType", "default");
        string contextPriority = systemContext.OperationalPriorities.FirstOrDefault() ?? "balanced";
        IReadOnlyList<OperationalStrategy> reusableStrategies = await _strategyStore.QueryByObjectiveTypeAsync(objectiveType, cancellationToken);
        OperationalStrategy? selectedStrategy = reusableStrategies.FirstOrDefault();

        string objectiveNodeId = $"objective:{perceivedObjective.Id}";
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
            perceivedObjective,
            optimizedWorkflow,
            candidates,
            cancellationToken);

        // Run scenario-level validation to assess multi-step risk and adjust strategy
        SimulationValidatedPlan validatedPlan = await _scenarioEngine.ValidatePlanAsync(
            perceivedObjective,
            optimizedWorkflow,
            candidates,
            cancellationToken);

        // Scenario simulation results influence the final strategy selection:
        // If the scenario engine overrides the comparison winner, use its recommendation
        string strategy = validatedPlan.SimulationApproved
            ? simulation.RecommendedStrategy
            : validatedPlan.ValidatedStrategy;

        // If scenario detected critical risk, apply its workflow adjustments
        if (!validatedPlan.SimulationApproved)
        {
            optimizedWorkflow = validatedPlan.Workflow;
        }

        string relatedSystemsCsv = string.Join(',', relatedSystems.Select(n => n.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase));

        string organizationId = perceivedObjective.Constraints.GetValueOrDefault("organizationId", "default-org");
        await _knowledgeGraphEngine.LinkWorkflowToOrganizationAsync(optimizedWorkflow.ObjectiveId, organizationId, cancellationToken);

        IReadOnlyList<KnowledgeNode> workflowOrganizations =
            await _knowledgeGraphEngine.GetOrganizationsForWorkflowAsync(optimizedWorkflow.ObjectiveId, cancellationToken);
        IReadOnlyList<KnowledgeNode> workflowDataSources =
            await _knowledgeGraphEngine.GetDataSourcesForWorkflowAsync(optimizedWorkflow.ObjectiveId, cancellationToken);

        string dataFabricSource = perceivedObjective.Constraints.GetValueOrDefault("dataFabricSource", "*");
        IReadOnlyList<string> plannerPermissions = new[] { "read:datafabric:planner" };
        var dataFabricResult = await _dataFabricEngine.QueryEnterpriseDataAsync(
            dataFabricSource,
            filters: new Dictionary<string, string>(),
            schemaMapping: new Dictionary<string, string>(),
            permissions: plannerPermissions,
            consumerType: "planner",
            cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var tasks = optimizedWorkflow.Steps
            .Select((step, index) => new { step, index })
            .Select(step => new CoreTask(
                Guid.NewGuid(),
                perceivedObjective.Id,
                step.step.Order,
                step.step.Name,
                step.step.Description,
                ResolveCapability(
                    ResolveRecommendedAgent(selectedStrategy, step.index, step.step.AgentType),
                    strategy),
                new Dictionary<string, string>(step.step.Inputs)
                {
                    ["contextPrimaryPriority"] = contextPriority,
                    ["contextOrganizationalGoals"] = string.Join("|", systemContext.OrganizationalGoals),
                    ["contextEnvironmentConstraints"] = string.Join("|", systemContext.EnvironmentConstraints.Select(kv => $"{kv.Key}={kv.Value}")),
                    ["contextHistoricalKnowledge"] = string.Join("|", systemContext.HistoricalKnowledge.Take(10)),
                    ["strategy"] = strategy,
                    ["objectiveType"] = objectiveType,
                    ["perceptionSource"] = perceptionResult.ExtractedContext.GetValueOrDefault("source", "external"),
                    ["perceptionPriority"] = perceptionResult.ExtractedContext.GetValueOrDefault("priority", contextPriority),
                    ["perceptionRemovedNoiseTokens"] = string.Join(",", perceptionResult.RemovedNoiseTokens),
                    ["strategySource"] = selectedStrategy is null ? "strategic-planner" : "reusable-strategy",
                    ["strategyTemplate"] = selectedStrategy?.WorkflowTemplate ?? optimizedWorkflow.Strategy,
                    ["strategyRecommendedAgents"] = selectedStrategy is null ? string.Empty : string.Join(',', selectedStrategy.RecommendedAgents),
                    ["relatedSystems"] = relatedSystemsCsv,
                    ["workflowOrganizations"] = string.Join(",", workflowOrganizations.Select(n => n.NodeId)),
                    ["workflowDataSources"] = string.Join(",", workflowDataSources.Select(n => n.NodeId)),
                    ["dataFabricAllowed"] = dataFabricResult.IsAllowed.ToString(),
                    ["dataFabricRows"] = dataFabricResult.Rows.Count.ToString(),
                    ["dataFabricReason"] = dataFabricResult.Reason,
                    ["simulationRecommendedStrategy"] = simulation.RecommendedStrategy,
                    ["simulationReason"] = simulation.Reason,
                    ["scenarioValidated"] = validatedPlan.SimulationApproved.ToString(),
                    ["scenarioStrategy"] = validatedPlan.ValidatedStrategy,
                    ["scenarioAdjustmentReason"] = validatedPlan.AdjustmentReason,
                    ["scenarioRiskLevel"] = validatedPlan.SimulationOutcome.RiskAssessment.RiskLevel,
                    ["scenarioSuccessProbability"] = validatedPlan.SimulationOutcome.OverallSuccessProbability.ToString("F3"),
                    ["scenarioRiskScore"] = validatedPlan.SimulationOutcome.OverallRiskScore.ToString("F3")
                },
                now,
                null,
                null))
            .OrderBy(task => task.Order)
            .ToArray();

        if (!_tenantResourceGovernor.CanCreateTaskCount(tenantId, tasks.Length))
        {
            throw new InvalidOperationException($"Tenant '{tenantId}' exceeded max tasks per plan.");
        }

        foreach (CoreTask plannedTask in tasks)
        {
            await _knowledgeGraphEngine.LinkTaskToWorkflowAsync(plannedTask.Id, optimizedWorkflow.ObjectiveId, cancellationToken);

            foreach (KnowledgeNode relatedSystem in relatedSystems)
            {
                await _knowledgeGraphEngine.LinkTaskToSystemAsync(plannedTask.Id, relatedSystem.NodeId, cancellationToken);

                string dataSourceId = relatedSystem.Properties.GetValueOrDefault("dataSourceId", string.Empty);
                if (!string.IsNullOrWhiteSpace(dataSourceId))
                {
                    await _knowledgeGraphEngine.LinkSystemToDataSourceAsync(relatedSystem.NodeId, dataSourceId, cancellationToken);
                }
            }
        }

        return tasks;
        }
        finally
        {
            _tenantResourceGovernor.ReleasePlanningSlot(tenantId);
        }
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

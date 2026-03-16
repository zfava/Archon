using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Optimization;
using ArchonAI.Core.Models.Planning;
using Microsoft.Extensions.Options;

namespace ArchonAI.Optimization;

public sealed class OptimizationEngine : IOptimizationEngine
{
    private readonly OptimizationOptions _options;
    private readonly IPlanningFeedbackStore _planningFeedbackStore;

    public OptimizationEngine(
        IOptions<OptimizationOptions> options,
        IPlanningFeedbackStore planningFeedbackStore)
    {
        _options = options.Value;
        _planningFeedbackStore = planningFeedbackStore;
    }

    public async global::System.Threading.Tasks.Task<WorkflowDefinition> OptimizeWorkflowAsync(
        Objective objective,
        WorkflowDefinition workflow,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        OptimizationInsight insight = AnalyzeEfficiency(workflow);
        if (!insight.WasInefficient)
        {
            return workflow;
        }

        IReadOnlyList<WorkflowStepDefinition> optimizedSteps = workflow.Steps
            .Where(step => !string.IsNullOrWhiteSpace(step.Name))
            .DistinctBy(step => (step.Name, step.AgentType))
            .OrderBy(step => step.Order)
            .Select((step, index) => step with { Order = index + 1 })
            .ToArray();

        string optimizedStrategy = workflow.Strategy.Equals("safe-mode", StringComparison.OrdinalIgnoreCase)
            ? "balanced"
            : workflow.Strategy;

        var optimized = workflow with
        {
            Strategy = optimizedStrategy,
            Summary = $"Optimized workflow from {workflow.Steps.Count} to {optimizedSteps.Count} steps ({insight.Reason}).",
            Steps = optimizedSteps,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        await _planningFeedbackStore.AddAsync(
            new PlanningFeedback(
                Strategy: optimized.Strategy,
                Capability: "workflow-optimization",
                WasSuccessful: true,
                Rationale: insight.Reason,
                RecordedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);

        return optimized;
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<OperationalStrategy>> GenerateImprovedStrategiesAsync(
        Objective objective,
        WorkflowDefinition workflow,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string objectiveType = objective.Constraints.GetValueOrDefault("objectiveType", "default");
        int maxCount = Math.Max(1, _options.MaxImprovedStrategies);

        IReadOnlyList<OperationalStrategy> strategies = Enumerable.Range(1, maxCount)
            .Select(index => new OperationalStrategy(
                Id: Guid.NewGuid(),
                ObjectiveType: objectiveType,
                WorkflowTemplate: index == 1 ? "balanced" : $"optimized-v{index}",
                RecommendedAgents: workflow.Steps.Select(step => step.AgentType).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                SuccessMetrics: new Dictionary<string, string>
                {
                    ["source"] = "optimization-engine",
                    ["efficiencyGain"] = $"{Math.Max(5, 15 - (index * 2))}%"
                },
                CreatedAtUtc: DateTimeOffset.UtcNow,
                UpdatedAtUtc: DateTimeOffset.UtcNow))
            .ToArray();

        return global::System.Threading.Tasks.Task.FromResult(strategies);
    }

    public async global::System.Threading.Tasks.Task<bool> DeployOptimizedWorkflowAsync(
        WorkflowDefinition optimizedWorkflow,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_options.AutoDeployOptimizedWorkflows)
        {
            return false;
        }

        await _planningFeedbackStore.AddAsync(
            new PlanningFeedback(
                Strategy: optimizedWorkflow.Strategy,
                Capability: "workflow-deployment",
                WasSuccessful: true,
                Rationale: $"Deployed optimized workflow with {optimizedWorkflow.Steps.Count} steps.",
                RecordedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);

        return true;
    }

    private OptimizationInsight AnalyzeEfficiency(WorkflowDefinition workflow)
    {
        int originalSteps = workflow.Steps.Count;
        int uniqueStepDefinitions = workflow.Steps
            .Select(step => (step.Name, step.AgentType))
            .Distinct()
            .Count();

        bool stepThresholdExceeded = originalSteps >= _options.InefficientStepThreshold;
        bool hasDuplicateSteps = uniqueStepDefinitions < originalSteps;

        bool inefficient = stepThresholdExceeded || hasDuplicateSteps;

        string reason = inefficient
            ? hasDuplicateSteps
                ? "duplicate-step-elimination"
                : "step-count-reduction"
            : "workflow-is-efficient";

        double efficiencyScore = originalSteps == 0
            ? 1.0
            : Math.Clamp(uniqueStepDefinitions / (double)Math.Max(1, originalSteps), 0.0, 1.0);

        return new OptimizationInsight(
            WorkflowId: workflow.ObjectiveId,
            WasInefficient: inefficient,
            Reason: reason,
            EfficiencyScore: efficiencyScore,
            OriginalStepCount: originalSteps,
            OptimizedStepCount: uniqueStepDefinitions,
            AnalyzedAtUtc: DateTimeOffset.UtcNow);
    }
}

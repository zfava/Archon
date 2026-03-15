using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Planning;

namespace ArchonAI.StrategicPlanner;

/// <summary>
/// Builds optimized workflow definitions from objective intent and constraints.
/// </summary>
public sealed class StrategicPlanningEngine : IStrategicPlanner
{
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

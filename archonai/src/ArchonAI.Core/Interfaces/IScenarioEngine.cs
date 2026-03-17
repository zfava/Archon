using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Simulation;

namespace ArchonAI.Core.Interfaces;

public interface IScenarioEngine
{
    global::System.Threading.Tasks.Task<ScenarioResult> RunScenarioAsync(
        ScenarioDefinition scenario,
        Objective objective,
        string strategy,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<ScenarioResult> EvaluateStrategyAsync(
        Objective objective,
        WorkflowDefinition workflow,
        string strategy,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<RiskAssessment> AssessRiskAsync(
        Objective objective,
        WorkflowDefinition workflow,
        string strategy,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<SimulationValidatedPlan> ValidatePlanAsync(
        Objective objective,
        WorkflowDefinition workflow,
        IReadOnlyList<string> candidateStrategies,
        CancellationToken cancellationToken = default);
}

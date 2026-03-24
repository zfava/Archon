using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Simulation;

namespace ArchonAI.Core.Interfaces;

public interface IStrategicPlanner
{
    global::System.Threading.Tasks.Task<WorkflowDefinition> BuildWorkflowAsync(
        Objective objective,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<SimulationValidatedPlan> BuildAndValidateWorkflowAsync(
        Objective objective,
        IReadOnlyList<string>? candidateStrategies = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Build task graphs for each candidate strategy, simulate all against the goal,
    /// and return the simulation-validated plan using the best strategy.
    /// </summary>
    global::System.Threading.Tasks.Task<SimulationGuidedPlan> BuildSimulationGuidedPlanAsync(
        OperationalGoal goal,
        IReadOnlyList<string>? candidateStrategies = null,
        CancellationToken cancellationToken = default);
}

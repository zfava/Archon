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
}

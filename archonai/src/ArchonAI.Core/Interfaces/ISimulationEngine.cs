using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Simulation;

namespace ArchonAI.Core.Interfaces;

public interface ISimulationEngine
{
    global::System.Threading.Tasks.Task<SimulationResult> SimulateWorkflowAsync(
        Objective objective,
        WorkflowDefinition workflow,
        string strategy,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<StrategyComparisonResult> CompareStrategiesAsync(
        Objective objective,
        WorkflowDefinition workflow,
        IReadOnlyList<string> strategies,
        CancellationToken cancellationToken = default);
}

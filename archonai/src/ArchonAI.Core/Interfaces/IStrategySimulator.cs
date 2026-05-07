using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Simulation;

namespace ArchonAI.Core.Interfaces;

public interface IStrategySimulator
{
    /// <summary>
    /// Simulate a single strategy against a task graph.
    /// </summary>
    global::System.Threading.Tasks.Task<StrategySimulationResult> SimulateAsync(
        TaskGraph graph,
        string strategy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Simulate multiple strategies against a task graph and recommend the best one.
    /// </summary>
    global::System.Threading.Tasks.Task<TaskGraphStrategyComparison> CompareStrategiesAsync(
        TaskGraph graph,
        IReadOnlyList<string> strategies,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Build task graphs for each strategy, simulate all, and return the best plan.
    /// Used by the planner to choose the final strategy before execution.
    /// </summary>
    global::System.Threading.Tasks.Task<TaskGraphStrategyComparison> SimulateGoalStrategiesAsync(
        OperationalGoal goal,
        IReadOnlyList<string>? candidateStrategies,
        CancellationToken cancellationToken = default);
}

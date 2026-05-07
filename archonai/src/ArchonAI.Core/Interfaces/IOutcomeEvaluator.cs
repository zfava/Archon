using ArchonAI.Core.Models.Evaluation;
using ArchonAI.Core.Models.Simulation;

namespace ArchonAI.Core.Interfaces;

public interface IOutcomeEvaluator
{
    /// <summary>
    /// Compare the expected outcome from a strategy simulation against actual execution
    /// results. Returns success metrics and stores evaluation in the KnowledgeGraph.
    /// </summary>
    global::System.Threading.Tasks.Task<OutcomeEvaluationResult> EvaluateAsync(
        StrategySimulationResult expectedSimulation,
        TaskGraphExecutionResult actualResult,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve stored evaluations for a goal from the KnowledgeGraph.
    /// </summary>
    global::System.Threading.Tasks.Task<IReadOnlyList<OutcomeEvaluationResult>> GetEvaluationsForGoalAsync(
        Guid goalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve stored evaluations for a strategy across all goals.
    /// </summary>
    global::System.Threading.Tasks.Task<IReadOnlyList<OutcomeEvaluationResult>> GetEvaluationsForStrategyAsync(
        string strategy,
        CancellationToken cancellationToken = default);
}

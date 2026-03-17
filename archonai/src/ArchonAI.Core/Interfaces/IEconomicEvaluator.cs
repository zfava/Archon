using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Reasoning;

namespace ArchonAI.Core.Interfaces;

public interface IEconomicEvaluator
{
    global::System.Threading.Tasks.Task<EconomicEvaluationResult> EvaluateStrategiesAsync(
        Objective objective,
        WorkflowDefinition workflow,
        IReadOnlyList<string> candidateStrategies,
        EconomicWeights? weights = null,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<StrategyEvaluation> EvaluateSingleStrategyAsync(
        Objective objective,
        WorkflowDefinition workflow,
        string strategy,
        CancellationToken cancellationToken = default);
}

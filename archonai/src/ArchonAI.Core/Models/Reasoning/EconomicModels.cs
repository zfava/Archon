namespace ArchonAI.Core.Models.Reasoning;

public sealed record StrategyEvaluation(
    string Strategy,
    decimal EstimatedCost,
    double ExpectedImpact,
    double ProbabilityOfSuccess,
    double EstimatedExecutionTimeHours,
    double EconomicScore,
    string ScoreBreakdown,
    IReadOnlyDictionary<string, double> FactorScores,
    DateTimeOffset EvaluatedAtUtc);

public sealed record EconomicEvaluationResult(
    IReadOnlyList<StrategyEvaluation> Evaluations,
    StrategyEvaluation BestStrategy,
    string SelectionRationale,
    EconomicWeights WeightsUsed,
    DateTimeOffset EvaluatedAtUtc);

public sealed record EconomicWeights(
    double CostWeight,
    double ImpactWeight,
    double SuccessProbabilityWeight,
    double ExecutionTimeWeight)
{
    public static EconomicWeights Default => new(
        CostWeight: 0.25,
        ImpactWeight: 0.35,
        SuccessProbabilityWeight: 0.25,
        ExecutionTimeWeight: 0.15);
}

using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Reasoning;
using ArchonAI.Core.Models.Simulation;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Reasoner;

/// <summary>
/// Evaluates candidate strategies using economic factors (cost, expected impact,
/// probability of success, execution time) and returns scored rankings so the
/// Reasoner / StrategicPlanner can select the best strategy.
/// </summary>
public sealed class EconomicEvaluator : IEconomicEvaluator
{
    private readonly ISimulationEngine _simulationEngine;
    private readonly IPlanningFeedbackStore _feedbackStore;
    private readonly ILogger<EconomicEvaluator> _logger;

    public EconomicEvaluator(
        ISimulationEngine simulationEngine,
        IPlanningFeedbackStore feedbackStore,
        ILogger<EconomicEvaluator> logger)
    {
        _simulationEngine = simulationEngine;
        _feedbackStore = feedbackStore;
        _logger = logger;
    }

    public async global::System.Threading.Tasks.Task<EconomicEvaluationResult> EvaluateStrategiesAsync(
        Objective objective,
        WorkflowDefinition workflow,
        IReadOnlyList<string> candidateStrategies,
        EconomicWeights? weights = null,
        CancellationToken cancellationToken = default)
    {
        weights ??= EconomicWeights.Default;

        var recentFeedback = await _feedbackStore.GetRecentAsync(50, cancellationToken);

        var evaluations = new List<StrategyEvaluation>();

        foreach (string strategy in candidateStrategies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var simulation = await _simulationEngine.SimulateWorkflowAsync(
                objective, workflow, strategy, cancellationToken);

            var evaluation = ScoreStrategy(strategy, simulation, objective, recentFeedback, weights);
            evaluations.Add(evaluation);
        }

        var sorted = evaluations.OrderByDescending(e => e.EconomicScore).ToList();
        var best = sorted[0];

        string rationale = BuildSelectionRationale(best, sorted);

        _logger.LogInformation(
            "Economic evaluation complete: {Count} strategies evaluated, best={Best} (score={Score:F3})",
            sorted.Count, best.Strategy, best.EconomicScore);

        return new EconomicEvaluationResult(
            Evaluations: sorted,
            BestStrategy: best,
            SelectionRationale: rationale,
            WeightsUsed: weights,
            EvaluatedAtUtc: DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task<StrategyEvaluation> EvaluateSingleStrategyAsync(
        Objective objective,
        WorkflowDefinition workflow,
        string strategy,
        CancellationToken cancellationToken = default)
    {
        var simulation = await _simulationEngine.SimulateWorkflowAsync(
            objective, workflow, strategy, cancellationToken);

        var recentFeedback = await _feedbackStore.GetRecentAsync(50, cancellationToken);

        return ScoreStrategy(strategy, simulation, objective, recentFeedback, EconomicWeights.Default);
    }

    // ══════════════════════════════════════════════════════════════
    //  Scoring
    // ══════════════════════════════════════════════════════════════

    private static StrategyEvaluation ScoreStrategy(
        string strategy,
        SimulationResult simulation,
        Objective objective,
        IReadOnlyList<PlanningFeedback> recentFeedback,
        EconomicWeights weights)
    {
        // ── Cost score ──────────────────────────────────────────
        // Lower cost is better. Normalize against a reference budget.
        decimal referenceBudget = ExtractBudget(objective);
        double costRatio = referenceBudget > 0
            ? (double)(simulation.PredictedCost / referenceBudget)
            : (double)simulation.PredictedCost / 100.0;
        double costScore = Math.Max(0, 1.0 - costRatio);

        // ── Impact score ────────────────────────────────────────
        // Probability of success directly maps to expected impact.
        // Boost if strategy historically succeeded.
        double historicalBoost = ComputeHistoricalBoost(strategy, recentFeedback);
        double impactScore = Math.Clamp(simulation.PredictedSuccessProbability + historicalBoost, 0, 1);

        // ── Success probability score ───────────────────────────
        // Direct from simulation, penalized by predicted risks.
        double riskPenalty = Math.Min(simulation.PredictedRisks.Count * 0.05, 0.3);
        double successScore = Math.Clamp(simulation.PredictedSuccessProbability - riskPenalty, 0, 1);

        // ── Execution time score ────────────────────────────────
        // Faster is better. Normalize against deadline or reference.
        double referenceHours = ExtractDeadlineHours(objective);
        double executionHours = simulation.PredictedLatencyMs / 3_600_000.0;
        double timeRatio = referenceHours > 0
            ? executionHours / referenceHours
            : executionHours / 24.0;
        double timeScore = Math.Max(0, 1.0 - timeRatio);

        // ── Weighted composite ──────────────────────────────────
        double compositeScore =
            costScore * weights.CostWeight +
            impactScore * weights.ImpactWeight +
            successScore * weights.SuccessProbabilityWeight +
            timeScore * weights.ExecutionTimeWeight;

        var factorScores = new Dictionary<string, double>
        {
            ["cost"] = costScore,
            ["impact"] = impactScore,
            ["successProbability"] = successScore,
            ["executionTime"] = timeScore,
            ["historicalBoost"] = historicalBoost,
            ["riskPenalty"] = riskPenalty
        };

        string breakdown =
            $"cost={costScore:F3}*{weights.CostWeight} + " +
            $"impact={impactScore:F3}*{weights.ImpactWeight} + " +
            $"success={successScore:F3}*{weights.SuccessProbabilityWeight} + " +
            $"time={timeScore:F3}*{weights.ExecutionTimeWeight} = {compositeScore:F3}";

        return new StrategyEvaluation(
            Strategy: strategy,
            EstimatedCost: simulation.PredictedCost,
            ExpectedImpact: impactScore,
            ProbabilityOfSuccess: successScore,
            EstimatedExecutionTimeHours: executionHours,
            EconomicScore: compositeScore,
            ScoreBreakdown: breakdown,
            FactorScores: factorScores,
            EvaluatedAtUtc: DateTimeOffset.UtcNow);
    }

    private static double ComputeHistoricalBoost(string strategy, IReadOnlyList<PlanningFeedback> feedback)
    {
        var relevant = feedback
            .Where(f => f.Strategy.Equals(strategy, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (relevant.Count == 0) return 0;

        double successRate = relevant.Count(f => f.WasSuccessful) / (double)relevant.Count;
        // Small boost/penalty based on historical track record
        return (successRate - 0.5) * 0.1;
    }

    private static decimal ExtractBudget(Objective objective)
    {
        if (objective.Constraints.TryGetValue("budget", out string? budgetStr) &&
            decimal.TryParse(budgetStr, out decimal budget))
        {
            return budget;
        }

        return 1000m; // default reference budget
    }

    private static double ExtractDeadlineHours(Objective objective)
    {
        if (objective.DueAtUtc is not null)
        {
            double hours = (objective.DueAtUtc.Value - DateTimeOffset.UtcNow).TotalHours;
            return hours > 0 ? hours : 24;
        }

        return 24; // default reference
    }

    private static string BuildSelectionRationale(
        StrategyEvaluation best,
        IReadOnlyList<StrategyEvaluation> sorted)
    {
        if (sorted.Count == 1)
            return $"Only one candidate strategy '{best.Strategy}' evaluated with score {best.EconomicScore:F3}.";

        var runner = sorted[1];
        double margin = best.EconomicScore - runner.EconomicScore;

        string confidence = margin switch
        {
            > 0.2 => "high",
            > 0.05 => "moderate",
            _ => "marginal"
        };

        return $"Strategy '{best.Strategy}' selected with {confidence} confidence " +
               $"(score {best.EconomicScore:F3} vs runner-up '{runner.Strategy}' at {runner.EconomicScore:F3}, " +
               $"margin {margin:F3}). " +
               $"Cost: {best.EstimatedCost:C2}, Impact: {best.ExpectedImpact:F2}, " +
               $"Success: {best.ProbabilityOfSuccess:F2}, Time: {best.EstimatedExecutionTimeHours:F1}h.";
    }
}

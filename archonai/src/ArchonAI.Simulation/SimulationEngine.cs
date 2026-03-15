using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Simulation;
using Microsoft.Extensions.Options;

namespace ArchonAI.Simulation;

public sealed class SimulationEngine : ISimulationEngine
{
    private readonly SimulationOptions _options;

    public SimulationEngine(IOptions<SimulationOptions> options)
    {
        _options = options.Value;
    }

    public global::System.Threading.Tasks.Task<SimulationResult> SimulateWorkflowAsync(
        Objective objective,
        WorkflowDefinition workflow,
        string strategy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int stepCount = Math.Max(1, workflow.Steps.Count);
        double riskFactor = ComputeRiskFactor(objective, strategy);

        double success = _options.BaseSuccessProbability - (_options.RiskPenaltyWeight * riskFactor);
        if (strategy.Equals("safe-mode", StringComparison.OrdinalIgnoreCase))
        {
            success += 0.12;
        }
        else if (strategy.Equals("throughput-optimized", StringComparison.OrdinalIgnoreCase))
        {
            success -= 0.05;
        }

        success = Math.Clamp(success, 0.05, 0.99);
        double failure = 1 - success;
        double latencyMs = stepCount * _options.LatencyWeightMs * (strategy.Equals("throughput-optimized", StringComparison.OrdinalIgnoreCase) ? 0.7 : 1.0);
        decimal cost = stepCount * _options.CostPerStep * (strategy.Equals("safe-mode", StringComparison.OrdinalIgnoreCase) ? 1.4m : 1.0m);

        var risks = new List<string>();
        if (riskFactor >= 1.5)
        {
            risks.Add("high-risk-objective");
        }
        if (objective.DueAtUtc is not null && (objective.DueAtUtc.Value - DateTimeOffset.UtcNow).TotalHours < 6)
        {
            risks.Add("tight-deadline");
        }
        if (strategy.Equals("throughput-optimized", StringComparison.OrdinalIgnoreCase))
        {
            risks.Add("reduced-validation-safety-margin");
        }

        return global::System.Threading.Tasks.Task.FromResult(new SimulationResult(
            SimulationId: Guid.NewGuid(),
            Strategy: strategy,
            PredictedSuccessProbability: success,
            PredictedFailureProbability: failure,
            PredictedLatencyMs: Math.Max(1, latencyMs),
            PredictedCost: Math.Max(0, cost),
            PredictedRisks: risks,
            SimulatedAtUtc: DateTimeOffset.UtcNow));
    }

    public async global::System.Threading.Tasks.Task<StrategyComparisonResult> CompareStrategiesAsync(
        Objective objective,
        WorkflowDefinition workflow,
        IReadOnlyList<string> strategies,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uniqueStrategies = strategies
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (uniqueStrategies.Length == 0)
        {
            uniqueStrategies = new[] { workflow.Strategy };
        }

        var results = new List<SimulationResult>();
        foreach (string strategy in uniqueStrategies)
        {
            results.Add(await SimulateWorkflowAsync(objective, workflow, strategy, cancellationToken));
        }

        SimulationResult winner = results
            .OrderByDescending(r => r.PredictedSuccessProbability)
            .ThenBy(r => r.PredictedFailureProbability)
            .ThenBy(r => r.PredictedCost)
            .ThenBy(r => r.PredictedLatencyMs)
            .First();

        return new StrategyComparisonResult(
            RecommendedStrategy: winner.Strategy,
            Results: results,
            Reason: $"Strategy '{winner.Strategy}' selected based on highest predicted success with balanced risk/cost.",
            ComparedAtUtc: DateTimeOffset.UtcNow);
    }

    private static double ComputeRiskFactor(Objective objective, string strategy)
    {
        double risk = 0;

        if (objective.Constraints.TryGetValue("risk", out string? riskLevel) && riskLevel.Equals("high", StringComparison.OrdinalIgnoreCase))
        {
            risk += 1.5;
        }

        if (objective.Constraints.TryGetValue("domain", out string? domain) && domain.Equals("financial", StringComparison.OrdinalIgnoreCase))
        {
            risk += 1.2;
        }

        if (strategy.Equals("throughput-optimized", StringComparison.OrdinalIgnoreCase))
        {
            risk += 0.7;
        }

        return risk;
    }
}

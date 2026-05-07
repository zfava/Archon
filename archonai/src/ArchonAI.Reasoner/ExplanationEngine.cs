using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Explanation;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Reasoning;
using ArchonAI.Registry;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Reasoner;

public sealed class ExplanationEngine : IExplanationEngine
{
    private readonly IEconomicEvaluator _economicEvaluator;
    private readonly IAgentCapabilityRegistry _capabilityRegistry;
    private readonly ILogger<ExplanationEngine> _logger;

    public ExplanationEngine(
        IEconomicEvaluator economicEvaluator,
        IAgentCapabilityRegistry capabilityRegistry,
        ILogger<ExplanationEngine> logger)
    {
        _economicEvaluator = economicEvaluator;
        _capabilityRegistry = capabilityRegistry;
        _logger = logger;
    }

    public async global::System.Threading.Tasks.Task<StrategyExplanation> ExplainStrategyChoiceAsync(
        Guid goalId,
        string goalTitle,
        IReadOnlyList<string> candidateStrategies,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Generating strategy explanation for goal {GoalId} with {Count} candidates",
            goalId, candidateStrategies.Count);

        var objective = new Objective(
            Id: goalId,
            Title: goalTitle,
            Description: goalTitle,
            Constraints: new Dictionary<string, string>(),
            CreatedAtUtc: DateTimeOffset.UtcNow,
            DueAtUtc: null);

        var workflow = new WorkflowDefinition(
            ObjectiveId: goalId,
            Strategy: candidateStrategies.FirstOrDefault() ?? "balanced",
            Summary: $"Workflow for {goalTitle}",
            Steps: [],
            CreatedAtUtc: DateTimeOffset.UtcNow);

        var evaluation = await _economicEvaluator.EvaluateStrategiesAsync(
            objective, workflow, candidateStrategies, cancellationToken: ct);

        var best = evaluation.BestStrategy;

        var factors = BuildStrategyFactors(best);

        var alternatives = evaluation.Evaluations
            .Where(e => e.Strategy != best.Strategy)
            .Select(e => new StrategyComparison(
                Strategy: e.Strategy,
                EconomicScore: e.EconomicScore,
                SuccessProbability: e.ProbabilityOfSuccess,
                EstimatedCost: e.EstimatedCost,
                EstimatedDurationHours: e.EstimatedExecutionTimeHours,
                WhyNotChosen: DeriveWhyNotChosen(best, e)))
            .ToList();

        return new StrategyExplanation(
            ExplanationId: Guid.NewGuid(),
            GoalId: goalId,
            GoalTitle: goalTitle,
            ChosenStrategy: best.Strategy,
            EconomicScore: best.EconomicScore,
            SelectionRationale: evaluation.SelectionRationale,
            Factors: factors,
            Alternatives: alternatives,
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task<AgentExplanation> ExplainAgentSelectionAsync(
        string requiredCapability,
        string? taskType,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Generating agent explanation for capability={Capability}, taskType={TaskType}",
            requiredCapability, taskType);

        var selection = await _capabilityRegistry.SelectBestAgentAsync(requiredCapability, taskType, ct);

        if (selection is null)
        {
            return new AgentExplanation(
                ExplanationId: Guid.NewGuid(),
                RequiredCapability: requiredCapability,
                TaskType: taskType,
                SelectedAgentId: Guid.Empty,
                SelectedAgentName: "None",
                SelectionScore: 0,
                SelectionReason: $"No agent found with capability '{requiredCapability}'.",
                Factors: [],
                Alternatives: [],
                GeneratedAtUtc: DateTimeOffset.UtcNow);
        }

        var allCandidates = await _capabilityRegistry.QueryByCapabilityAsync(requiredCapability, ct);

        var factors = BuildAgentFactors(selection);

        var alternatives = allCandidates
            .Where(a => a.AgentId != selection.AgentId)
            .OrderByDescending(a => a.SuccessRate)
            .Take(5)
            .Select(a => new AgentAlternative(
                AgentId: a.AgentId,
                AgentName: a.AgentName,
                Score: ComputeAgentScore(a),
                SuccessRate: a.SuccessRate,
                AverageLatencyMs: a.AverageLatencyMs,
                AverageCost: a.AverageCost,
                WhyNotChosen: DeriveAgentWhyNotChosen(selection, a)))
            .ToList();

        return new AgentExplanation(
            ExplanationId: Guid.NewGuid(),
            RequiredCapability: requiredCapability,
            TaskType: taskType,
            SelectedAgentId: selection.AgentId,
            SelectedAgentName: selection.AgentName,
            SelectionScore: selection.Score,
            SelectionReason: selection.SelectionReason,
            Factors: factors,
            Alternatives: alternatives,
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task<DecisionExplanation> ExplainDecisionAsync(
        Guid goalId,
        string goalTitle,
        IReadOnlyList<string> candidateStrategies,
        string requiredCapability,
        string? taskType,
        CancellationToken ct = default)
    {
        var strategyExplanation = await ExplainStrategyChoiceAsync(goalId, goalTitle, candidateStrategies, ct);
        var agentExplanation = await ExplainAgentSelectionAsync(requiredCapability, taskType, ct);

        var summary = $"Strategy '{strategyExplanation.ChosenStrategy}' selected (score: {strategyExplanation.EconomicScore:F2}). " +
                      $"Agent '{agentExplanation.SelectedAgentName}' chosen for '{requiredCapability}' (score: {agentExplanation.SelectionScore:F2}).";

        return new DecisionExplanation(
            ExplanationId: Guid.NewGuid(),
            DecisionType: "StrategyAndAgent",
            Summary: summary,
            StrategyExplanation: strategyExplanation,
            AgentExplanation: agentExplanation,
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    private static List<StrategyFactor> BuildStrategyFactors(StrategyEvaluation eval)
    {
        var factors = new List<StrategyFactor>();

        foreach (var (name, score) in eval.FactorScores)
        {
            var weight = name switch
            {
                "cost" => EconomicWeights.Default.CostWeight,
                "impact" => EconomicWeights.Default.ImpactWeight,
                "successProbability" => EconomicWeights.Default.SuccessProbabilityWeight,
                "executionTime" => EconomicWeights.Default.ExecutionTimeWeight,
                _ => 0.0
            };

            var impact = score >= 0.7 ? "High" : score >= 0.4 ? "Medium" : "Low";
            var description = name switch
            {
                "cost" => $"Estimated cost: ${eval.EstimatedCost:F2}. Lower cost improves economic viability.",
                "impact" => $"Expected business impact: {eval.ExpectedImpact:F1}. Higher impact means greater value.",
                "successProbability" => $"Success probability: {eval.ProbabilityOfSuccess:P0}. Higher likelihood of achieving the goal.",
                "executionTime" => $"Estimated duration: {eval.EstimatedExecutionTimeHours:F1}h. Faster execution reduces risk window.",
                _ => $"Factor '{name}' scored {score:F2}."
            };

            factors.Add(new StrategyFactor(name, score, weight, impact, description));
        }

        if (factors.Count == 0)
        {
            factors.Add(new StrategyFactor("economicScore", eval.EconomicScore, 1.0, "High",
                $"Overall economic score: {eval.EconomicScore:F2}. {eval.ScoreBreakdown}"));
        }

        return factors;
    }

    private static List<AgentFactor> BuildAgentFactors(AgentSelectionResult selection)
    {
        return
        [
            new AgentFactor("successRate", $"{selection.SuccessRate:P0}",
                selection.SuccessRate >= 0.9 ? "High" : selection.SuccessRate >= 0.7 ? "Medium" : "Low",
                $"Historical success rate of {selection.SuccessRate:P0} across past executions."),
            new AgentFactor("latency", $"{selection.AverageLatencyMs:F0}ms",
                selection.AverageLatencyMs <= 500 ? "High" : selection.AverageLatencyMs <= 2000 ? "Medium" : "Low",
                $"Average response latency of {selection.AverageLatencyMs:F0}ms."),
            new AgentFactor("cost", $"${selection.AverageCost:F2}",
                selection.AverageCost <= 1.0m ? "High" : selection.AverageCost <= 5.0m ? "Medium" : "Low",
                $"Average cost per execution: ${selection.AverageCost:F2}."),
            new AgentFactor("overallScore", $"{selection.Score:F2}",
                selection.Score >= 0.8 ? "High" : selection.Score >= 0.5 ? "Medium" : "Low",
                $"Composite selection score: {selection.Score:F2}. Combines success rate, latency, and cost.")
        ];
    }

    private static double ComputeAgentScore(AgentCapabilityProfile profile)
    {
        var latencyScore = Math.Max(0, 1.0 - profile.AverageLatencyMs / 5000.0);
        var costScore = Math.Max(0, 1.0 - (double)profile.AverageCost / 10.0);
        return profile.SuccessRate * 0.5 + latencyScore * 0.3 + costScore * 0.2;
    }

    private static string DeriveWhyNotChosen(StrategyEvaluation best, StrategyEvaluation alt)
    {
        var reasons = new List<string>();

        if (alt.EconomicScore < best.EconomicScore)
            reasons.Add($"lower economic score ({alt.EconomicScore:F2} vs {best.EconomicScore:F2})");
        if (alt.ProbabilityOfSuccess < best.ProbabilityOfSuccess)
            reasons.Add($"lower success probability ({alt.ProbabilityOfSuccess:P0} vs {best.ProbabilityOfSuccess:P0})");
        if (alt.EstimatedCost > best.EstimatedCost)
            reasons.Add($"higher cost (${alt.EstimatedCost:F2} vs ${best.EstimatedCost:F2})");
        if (alt.EstimatedExecutionTimeHours > best.EstimatedExecutionTimeHours)
            reasons.Add($"longer execution ({alt.EstimatedExecutionTimeHours:F1}h vs {best.EstimatedExecutionTimeHours:F1}h)");

        return reasons.Count > 0
            ? string.Join("; ", reasons)
            : "Similar performance but lower composite score.";
    }

    private static string DeriveAgentWhyNotChosen(AgentSelectionResult best, AgentCapabilityProfile alt)
    {
        var reasons = new List<string>();

        if (alt.SuccessRate < best.SuccessRate)
            reasons.Add($"lower success rate ({alt.SuccessRate:P0} vs {best.SuccessRate:P0})");
        if (alt.AverageLatencyMs > best.AverageLatencyMs)
            reasons.Add($"higher latency ({alt.AverageLatencyMs:F0}ms vs {best.AverageLatencyMs:F0}ms)");
        if (alt.AverageCost > best.AverageCost)
            reasons.Add($"higher cost (${alt.AverageCost:F2} vs ${best.AverageCost:F2})");

        return reasons.Count > 0
            ? string.Join("; ", reasons)
            : "Similar performance but lower composite score.";
    }
}

using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Simulation;
using Microsoft.Extensions.Options;

namespace ArchonAI.Simulation;

public sealed class ScenarioEngine : IScenarioEngine
{
    private readonly SimulationOptions _options;
    private readonly ISimulationEngine _simulationEngine;

    public ScenarioEngine(IOptions<SimulationOptions> options, ISimulationEngine simulationEngine)
    {
        _options = options.Value;
        _simulationEngine = simulationEngine;
    }

    public global::System.Threading.Tasks.Task<ScenarioResult> RunScenarioAsync(
        ScenarioDefinition scenario,
        Objective objective,
        string strategy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stepResults = new List<ScenarioStepResult>();
        double cumulativeSuccess = 1.0;
        double totalLatency = 0;
        decimal totalCost = 0;
        double totalRisk = 0;

        foreach (ScenarioStep step in scenario.Steps.OrderBy(s => s.Order))
        {
            double stepBaseSuccess = _options.BaseSuccessProbability;
            double stepRisk = ComputeStepRisk(step, objective, strategy);

            // Dependency penalty: steps with more dependencies are riskier
            double dependencyPenalty = step.DependsOn.Count * _options.DependencyRiskPenalty;
            double stepSuccess = Math.Clamp(stepBaseSuccess - (_options.RiskPenaltyWeight * stepRisk) - dependencyPenalty, 0.05, 0.99);

            // Strategy modifiers
            stepSuccess = ApplyStrategyModifier(stepSuccess, strategy);

            double stepLatency = _options.LatencyWeightMs * (strategy.Equals("throughput-optimized", StringComparison.OrdinalIgnoreCase) ? 0.7 : 1.0);
            decimal stepCost = _options.CostPerStep * (strategy.Equals("safe-mode", StringComparison.OrdinalIgnoreCase) ? 1.4m : 1.0m);

            var warnings = new List<string>();
            if (stepRisk > 1.0) warnings.Add($"high-risk-step:{step.Name}");
            if (step.DependsOn.Count > 2) warnings.Add($"complex-dependency-chain:{step.Name}");

            stepResults.Add(new ScenarioStepResult(
                Order: step.Order,
                StepName: step.Name,
                Passed: stepSuccess >= _options.StepPassThreshold,
                SuccessProbability: stepSuccess,
                RiskContribution: stepRisk,
                EstimatedLatencyMs: stepLatency,
                EstimatedCost: stepCost,
                Warnings: warnings));

            cumulativeSuccess *= stepSuccess;
            totalLatency += stepLatency;
            totalCost += stepCost;
            totalRisk += stepRisk;
        }

        int stepCount = Math.Max(1, scenario.Steps.Count);
        double aggregateRisk = stepCount > 0 ? totalRisk / stepCount : 0;
        var riskAssessment = BuildRiskAssessment(objective, strategy, aggregateRisk, stepResults);
        bool allPassed = stepResults.All(s => s.Passed);

        return global::System.Threading.Tasks.Task.FromResult(new ScenarioResult(
            ScenarioId: scenario.ScenarioId,
            ScenarioName: scenario.Name,
            Strategy: strategy,
            PassedAllSteps: allPassed,
            OverallSuccessProbability: Math.Clamp(cumulativeSuccess, 0.001, 0.99),
            OverallRiskScore: aggregateRisk,
            StepResults: stepResults,
            RiskAssessment: riskAssessment,
            EstimatedTotalCost: totalCost,
            EstimatedTotalLatencyMs: totalLatency,
            EvaluatedAtUtc: DateTimeOffset.UtcNow));
    }

    public async global::System.Threading.Tasks.Task<ScenarioResult> EvaluateStrategyAsync(
        Objective objective,
        WorkflowDefinition workflow,
        string strategy,
        CancellationToken cancellationToken = default)
    {
        ScenarioDefinition scenario = BuildScenarioFromWorkflow(workflow);
        return await RunScenarioAsync(scenario, objective, strategy, cancellationToken);
    }

    public global::System.Threading.Tasks.Task<RiskAssessment> AssessRiskAsync(
        Objective objective,
        WorkflowDefinition workflow,
        string strategy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var factors = new List<RiskFactor>();

        // Objective risk factors
        if (objective.Constraints.TryGetValue("risk", out string? riskLevel) &&
            riskLevel.Equals("high", StringComparison.OrdinalIgnoreCase))
        {
            factors.Add(new RiskFactor("objective-risk-level", 0.3, 0.9, "Objective is flagged as high risk."));
        }

        if (objective.Constraints.TryGetValue("domain", out string? domain) &&
            domain.Equals("financial", StringComparison.OrdinalIgnoreCase))
        {
            factors.Add(new RiskFactor("financial-domain", 0.25, 0.8, "Financial domain requires elevated caution."));
        }

        if (objective.DueAtUtc is not null && (objective.DueAtUtc.Value - DateTimeOffset.UtcNow).TotalHours < 6)
        {
            factors.Add(new RiskFactor("tight-deadline", 0.2, 0.85, "Less than 6 hours until deadline."));
        }

        // Workflow complexity factors
        int stepCount = workflow.Steps.Count;
        if (stepCount > 6)
        {
            factors.Add(new RiskFactor("workflow-complexity", 0.15, Math.Min(1.0, stepCount / 10.0), $"Workflow has {stepCount} steps."));
        }

        // Strategy suitability
        double strategyRisk = strategy switch
        {
            "safe-mode" => 0.1,
            "balanced" => 0.3,
            "cost-optimized" => 0.5,
            "throughput-optimized" => 0.7,
            _ => 0.4
        };
        factors.Add(new RiskFactor("strategy-risk-profile", 0.1, strategyRisk, $"Strategy '{strategy}' risk profile."));

        double aggregateScore = factors.Count > 0
            ? factors.Sum(f => f.Weight * f.Score) / factors.Sum(f => f.Weight)
            : 0;

        string level = aggregateScore switch
        {
            >= 0.7 => "critical",
            >= 0.5 => "high",
            >= 0.3 => "moderate",
            _ => "low"
        };

        string recommendation = level switch
        {
            "critical" => "Switch to safe-mode strategy and increase validation checkpoints.",
            "high" => "Consider safe-mode or add additional validation steps.",
            "moderate" => "Proceed with current strategy but monitor closely.",
            _ => "Proceed as planned."
        };

        return global::System.Threading.Tasks.Task.FromResult(new RiskAssessment(
            AggregateRiskScore: aggregateScore,
            RiskLevel: level,
            Factors: factors,
            Recommendation: recommendation));
    }

    public async global::System.Threading.Tasks.Task<SimulationValidatedPlan> ValidatePlanAsync(
        Objective objective,
        WorkflowDefinition workflow,
        IReadOnlyList<string> candidateStrategies,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var strategies = candidateStrategies
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (strategies.Count == 0)
        {
            strategies.Add(workflow.Strategy);
        }

        // Run scenario simulation for each candidate
        ScenarioResult? bestResult = null;
        string bestStrategy = workflow.Strategy;

        foreach (string candidate in strategies)
        {
            var result = await EvaluateStrategyAsync(objective, workflow, candidate, cancellationToken);

            if (bestResult is null ||
                result.OverallSuccessProbability > bestResult.OverallSuccessProbability ||
                (Math.Abs(result.OverallSuccessProbability - bestResult.OverallSuccessProbability) < 0.01 &&
                 result.OverallRiskScore < bestResult.OverallRiskScore))
            {
                bestResult = result;
                bestStrategy = candidate;
            }
        }

        // Determine if original strategy needs adjustment
        bool approved = bestStrategy.Equals(workflow.Strategy, StringComparison.OrdinalIgnoreCase);
        string adjustmentReason = approved
            ? "Original strategy validated by simulation."
            : $"Simulation recommends switching from '{workflow.Strategy}' to '{bestStrategy}' " +
              $"(success: {bestResult!.OverallSuccessProbability:P1}, risk: {bestResult.RiskAssessment.RiskLevel}).";

        // If risk is critical, force safe-mode regardless
        if (bestResult!.RiskAssessment.RiskLevel == "critical" && bestStrategy != "safe-mode")
        {
            var safeModeResult = await EvaluateStrategyAsync(objective, workflow, "safe-mode", cancellationToken);
            bestResult = safeModeResult;
            bestStrategy = "safe-mode";
            approved = false;
            adjustmentReason = "Simulation detected critical risk; forcing safe-mode strategy.";
        }

        // Rebuild workflow with validated strategy if changed
        WorkflowDefinition validatedWorkflow = approved
            ? workflow
            : workflow with { Strategy = bestStrategy };

        return new SimulationValidatedPlan(
            Workflow: validatedWorkflow,
            ValidatedStrategy: bestStrategy,
            SimulationOutcome: bestResult,
            SimulationApproved: approved,
            AdjustmentReason: adjustmentReason,
            ValidatedAtUtc: DateTimeOffset.UtcNow);
    }

    private static ScenarioDefinition BuildScenarioFromWorkflow(WorkflowDefinition workflow)
    {
        var scenarioSteps = workflow.Steps
            .Select(step => new ScenarioStep(
                Order: step.Order,
                Name: step.Name,
                Action: step.Description,
                Inputs: step.Inputs,
                DependsOn: step.Order > 10
                    ? new List<string> { workflow.Steps.Where(s => s.Order < step.Order).MaxBy(s => s.Order)?.Name ?? "" }
                    : new List<string>()))
            .ToList();

        return new ScenarioDefinition(
            ScenarioId: Guid.NewGuid(),
            Name: $"scenario:{workflow.Strategy}:{workflow.ObjectiveId}",
            Description: workflow.Summary,
            Steps: scenarioSteps,
            EnvironmentParameters: new Dictionary<string, string>
            {
                ["strategy"] = workflow.Strategy,
                ["objectiveId"] = workflow.ObjectiveId.ToString()
            },
            CreatedAtUtc: DateTimeOffset.UtcNow);
    }

    private double ComputeStepRisk(ScenarioStep step, Objective objective, string strategy)
    {
        double risk = 0;

        if (objective.Constraints.TryGetValue("risk", out string? riskLevel) &&
            riskLevel.Equals("high", StringComparison.OrdinalIgnoreCase))
        {
            risk += 1.5;
        }

        if (objective.Constraints.TryGetValue("domain", out string? domain) &&
            domain.Equals("financial", StringComparison.OrdinalIgnoreCase))
        {
            risk += 1.2;
        }

        if (strategy.Equals("throughput-optimized", StringComparison.OrdinalIgnoreCase))
        {
            risk += 0.7;
        }

        // Steps later in the pipeline carry compounding risk
        risk += step.Order * 0.01;

        return risk;
    }

    private static double ApplyStrategyModifier(double baseSuccess, string strategy)
    {
        return strategy.ToLowerInvariant() switch
        {
            "safe-mode" => Math.Clamp(baseSuccess + 0.12, 0.05, 0.99),
            "throughput-optimized" => Math.Clamp(baseSuccess - 0.05, 0.05, 0.99),
            "cost-optimized" => Math.Clamp(baseSuccess - 0.02, 0.05, 0.99),
            _ => baseSuccess
        };
    }

    private RiskAssessment BuildRiskAssessment(Objective objective, string strategy, double aggregateRisk, IReadOnlyList<ScenarioStepResult> stepResults)
    {
        var factors = new List<RiskFactor>();

        int failedSteps = stepResults.Count(s => !s.Passed);
        if (failedSteps > 0)
        {
            factors.Add(new RiskFactor("step-failures", 0.4, Math.Min(1.0, failedSteps / 3.0), $"{failedSteps} step(s) predicted to fail."));
        }

        if (aggregateRisk > 1.0)
        {
            factors.Add(new RiskFactor("high-aggregate-risk", 0.3, Math.Min(1.0, aggregateRisk / 3.0), "Aggregate risk across steps is elevated."));
        }

        double strategyRisk = strategy switch
        {
            "safe-mode" => 0.1,
            "balanced" => 0.3,
            "cost-optimized" => 0.5,
            "throughput-optimized" => 0.7,
            _ => 0.4
        };
        factors.Add(new RiskFactor("strategy-profile", 0.3, strategyRisk, $"Strategy '{strategy}' inherent risk."));

        double score = factors.Count > 0
            ? factors.Sum(f => f.Weight * f.Score) / factors.Sum(f => f.Weight)
            : 0;

        string level = score switch
        {
            >= 0.7 => "critical",
            >= 0.5 => "high",
            >= 0.3 => "moderate",
            _ => "low"
        };

        string recommendation = level switch
        {
            "critical" => "Switch to safe-mode strategy and increase validation checkpoints.",
            "high" => "Consider safe-mode or add additional validation steps.",
            "moderate" => "Proceed with monitoring.",
            _ => "Proceed as planned."
        };

        return new RiskAssessment(score, level, factors, recommendation);
    }
}

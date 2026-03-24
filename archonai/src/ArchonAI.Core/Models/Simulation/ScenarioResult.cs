namespace ArchonAI.Core.Models.Simulation;

public sealed record ScenarioResult(
    Guid ScenarioId,
    string ScenarioName,
    string Strategy,
    bool PassedAllSteps,
    double OverallSuccessProbability,
    double OverallRiskScore,
    IReadOnlyList<ScenarioStepResult> StepResults,
    RiskAssessment RiskAssessment,
    decimal EstimatedTotalCost,
    double EstimatedTotalLatencyMs,
    DateTimeOffset EvaluatedAtUtc);

public sealed record ScenarioStepResult(
    int Order,
    string StepName,
    bool Passed,
    double SuccessProbability,
    double RiskContribution,
    double EstimatedLatencyMs,
    decimal EstimatedCost,
    IReadOnlyList<string> Warnings);

public sealed record RiskAssessment(
    double AggregateRiskScore,
    string RiskLevel,
    IReadOnlyList<RiskFactor> Factors,
    string Recommendation);

public sealed record RiskFactor(
    string Name,
    double Weight,
    double Score,
    string Description);

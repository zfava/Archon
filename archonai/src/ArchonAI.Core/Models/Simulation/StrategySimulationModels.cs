using ArchonAI.Core.Models.Planning;

namespace ArchonAI.Core.Models.Simulation;

// ══════════════════════════════════════════════════════════════
//  Simulation result for a single strategy + TaskGraph
// ══════════════════════════════════════════════════════════════

public sealed record StrategySimulationResult(
    Guid SimulationId,
    Guid GraphId,
    string Strategy,
    ExpectedOutcome ExpectedOutcome,
    double RiskScore,
    string RiskLevel,
    IReadOnlyList<SimulatedNodeResult> NodeResults,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Warnings,
    double EstimatedTotalDurationHours,
    decimal EstimatedTotalCost,
    DateTimeOffset SimulatedAtUtc);

public sealed record ExpectedOutcome(
    double OverallSuccessProbability,
    bool PredictedSuccess,
    string PredictedOutcomeLabel,
    double Confidence,
    int TotalNodes,
    int CriticalPathLength,
    int ParallelismDegree);

public sealed record SimulatedNodeResult(
    Guid NodeId,
    string NodeName,
    string AgentType,
    double SuccessProbability,
    double EstimatedDurationHours,
    decimal EstimatedCost,
    bool IsOnCriticalPath,
    IReadOnlyList<string> NodeRisks);

// ══════════════════════════════════════════════════════════════
//  Comparative result across strategies
// ══════════════════════════════════════════════════════════════

public sealed record TaskGraphStrategyComparison(
    Guid GoalId,
    IReadOnlyList<StrategySimulationResult> Simulations,
    StrategySimulationResult RecommendedSimulation,
    string RecommendationReason,
    DateTimeOffset ComparedAtUtc);

// ══════════════════════════════════════════════════════════════
//  Options
// ══════════════════════════════════════════════════════════════

public sealed record StrategySimulatorOptions
{
    public const string SectionName = "StrategySimulation";

    // Base success probabilities by agent type
    public double ContextAnalysisSuccessProb { get; set; } = 0.95;
    public double WorkflowOrchestrationSuccessProb { get; set; } = 0.90;
    public double OperationExecutionSuccessProb { get; set; } = 0.85;
    public double OutcomeValidationSuccessProb { get; set; } = 0.97;
    public double FinancialOperationsSuccessProb { get; set; } = 0.88;
    public double DefaultSuccessProb { get; set; } = 0.85;

    // Cost per hour by agent type
    public decimal ContextAnalysisCostPerHour { get; set; } = 0.50m;
    public decimal WorkflowOrchestrationCostPerHour { get; set; } = 0.75m;
    public decimal OperationExecutionCostPerHour { get; set; } = 1.00m;
    public decimal OutcomeValidationCostPerHour { get; set; } = 0.30m;
    public decimal DefaultCostPerHour { get; set; } = 0.60m;

    // Strategy modifiers
    public double SafeModeSuccessBoost { get; set; } = 0.05;
    public double SafeModeDurationMultiplier { get; set; } = 1.5;
    public double ThroughputDurationMultiplier { get; set; } = 0.7;
    public double ThroughputSuccessPenalty { get; set; } = 0.05;
    public double CostOptimizedCostMultiplier { get; set; } = 0.6;
    public double CostOptimizedSuccessPenalty { get; set; } = 0.03;

    // Risk thresholds
    public double HighRiskThreshold { get; set; } = 0.6;
    public double MediumRiskThreshold { get; set; } = 0.3;
    public double NodeRiskThreshold { get; set; } = 0.75;
    public double MinAcceptableSuccessProb { get; set; } = 0.5;
}

using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Simulation;

namespace ArchonAI.Core.Models.Evaluation;

// ══════════════════════════════════════════════════════════════
//  Input: actual execution results for a TaskGraph
// ══════════════════════════════════════════════════════════════

public sealed record TaskGraphExecutionResult(
    Guid GraphId,
    Guid GoalId,
    string Strategy,
    bool OverallSuccess,
    IReadOnlyList<TaskNodeExecutionResult> NodeResults,
    double TotalDurationHours,
    decimal TotalCost,
    DateTimeOffset CompletedAtUtc);

public sealed record TaskNodeExecutionResult(
    Guid NodeId,
    string NodeName,
    string AgentType,
    bool Succeeded,
    double DurationHours,
    decimal Cost,
    int AttemptCount,
    IReadOnlyList<string> Errors,
    DateTimeOffset CompletedAtUtc);

// ══════════════════════════════════════════════════════════════
//  Output: evaluation comparing expected vs actual
// ══════════════════════════════════════════════════════════════

public sealed record OutcomeEvaluationResult(
    Guid EvaluationId,
    Guid GraphId,
    Guid GoalId,
    string Strategy,
    SuccessMetrics SuccessMetrics,
    ExpectedVsActual Comparison,
    IReadOnlyList<NodeEvaluationResult> NodeEvaluations,
    IReadOnlyList<string> Insights,
    IReadOnlyList<string> Recommendations,
    string OverallAssessment,
    DateTimeOffset EvaluatedAtUtc);

public sealed record SuccessMetrics(
    double SuccessRate,
    double AccuracyScore,
    double DurationAccuracy,
    double CostAccuracy,
    double RiskPredictionAccuracy,
    int TotalNodes,
    int SucceededNodes,
    int FailedNodes,
    double OverallScore);

public sealed record ExpectedVsActual(
    double ExpectedSuccessProbability,
    bool ActualSuccess,
    double ExpectedDurationHours,
    double ActualDurationHours,
    double DurationDeviationPercent,
    decimal ExpectedCost,
    decimal ActualCost,
    double CostDeviationPercent,
    double ExpectedRiskScore,
    bool RiskPredictionCorrect);

public sealed record NodeEvaluationResult(
    Guid NodeId,
    string NodeName,
    string AgentType,
    double PredictedSuccessProbability,
    bool ActualSuccess,
    double PredictedDurationHours,
    double ActualDurationHours,
    decimal PredictedCost,
    decimal ActualCost,
    bool WasOnCriticalPath,
    string Assessment);

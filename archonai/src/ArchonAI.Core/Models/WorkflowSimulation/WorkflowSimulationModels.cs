namespace ArchonAI.Core.Models.WorkflowSimulation;

// ── Simulation request ───────────────────────────────────────────────

/// <summary>
/// Input for a workflow simulation run.
/// </summary>
public sealed record WorkflowSimulationRequest(
    Guid WorkflowGraphId,
    IReadOnlyDictionary<string, string>? HistoricalOverrides,
    Guid? StrategyId);

// ── Simulation result ────────────────────────────────────────────────

/// <summary>
/// Full result of a workflow simulation run.
/// </summary>
public sealed record WorkflowSimulationRunResult(
    Guid SimulationId,
    Guid WorkflowGraphId,
    Guid? StrategyId,
    bool PredictedSuccess,
    double PredictedSuccessProbability,
    double EstimatedLatencyMs,
    double EstimatedCost,
    WorkflowResourceEstimate ResourceEstimate,
    IReadOnlyList<SimulatedStep> Steps,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Warnings,
    DateTimeOffset SimulatedAtUtc);

/// <summary>
/// A single step in the simulation trace.
/// </summary>
public sealed record SimulatedStep(
    Guid NodeId,
    string NodeName,
    string NodeType,
    int Order,
    string PredictedOutcome,
    double PredictedDurationMs,
    double SuccessProbability);

// ── Resource estimation ──────────────────────────────────────────────

/// <summary>
/// Predicted resource consumption for a workflow execution.
/// </summary>
public sealed record WorkflowResourceEstimate(
    double EstimatedCpuSeconds,
    double EstimatedMemoryMb,
    int EstimatedAgentCount,
    double EstimatedTotalCost,
    string CostCurrency);

// ── Outcome prediction ───────────────────────────────────────────────

/// <summary>
/// Predicted outcomes for a workflow, including per-node predictions.
/// </summary>
public sealed record WorkflowOutcomePrediction(
    Guid SimulationId,
    Guid WorkflowGraphId,
    double OverallSuccessProbability,
    double OverallFailureProbability,
    IReadOnlyList<NodeOutcomePrediction> NodePredictions,
    IReadOnlyList<string> CriticalPathNodes,
    DateTimeOffset PredictedAtUtc);

public sealed record NodeOutcomePrediction(
    Guid NodeId,
    string NodeName,
    double SuccessProbability,
    double EstimatedDurationMs,
    string MostLikelyOutcome);

// ── Latency estimate ─────────────────────────────────────────────────

/// <summary>
/// Execution latency breakdown.
/// </summary>
public sealed record WorkflowLatencyEstimate(
    Guid SimulationId,
    Guid WorkflowGraphId,
    double TotalEstimatedMs,
    double CriticalPathMs,
    double P50EstimatedMs,
    double P95EstimatedMs,
    IReadOnlyList<NodeLatencyEstimate> NodeEstimates,
    DateTimeOffset EstimatedAtUtc);

public sealed record NodeLatencyEstimate(
    Guid NodeId,
    string NodeName,
    double EstimatedMs,
    double P95EstimatedMs,
    bool IsOnCriticalPath);

// ── Historical execution data ────────────────────────────────────────

/// <summary>
/// Aggregated historical data for a workflow, used to calibrate predictions.
/// </summary>
public sealed record WorkflowHistoricalData(
    Guid WorkflowGraphId,
    long TotalExecutions,
    double HistoricalSuccessRate,
    double HistoricalAverageLatencyMs,
    double HistoricalP95LatencyMs,
    double HistoricalAverageCost,
    DateTimeOffset LastExecutionAtUtc);

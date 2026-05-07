using ArchonAI.Core.Models.WorkflowSimulation;

namespace ArchonAI.Core.Interfaces;

/// <summary>
/// High-level service for simulating workflow execution before running it.
/// Coordinates the simulation engine with workflow definitions, historical
/// execution data, and the strategy library.
/// </summary>
public interface IWorkflowSimulationService
{
    /// <summary>Runs a full simulation of a workflow graph.</summary>
    global::System.Threading.Tasks.Task<WorkflowSimulationRunResult> SimulateAsync(
        Guid workflowGraphId, Guid? strategyId = null,
        IReadOnlyDictionary<string, string>? historicalOverrides = null,
        CancellationToken ct = default);

    /// <summary>Predicts outcomes per node.</summary>
    global::System.Threading.Tasks.Task<WorkflowOutcomePrediction> PredictOutcomesAsync(
        Guid workflowGraphId, CancellationToken ct = default);

    /// <summary>Estimates resource usage for a workflow execution.</summary>
    global::System.Threading.Tasks.Task<WorkflowResourceEstimate> EstimateResourcesAsync(
        Guid workflowGraphId, Guid? strategyId = null,
        CancellationToken ct = default);

    /// <summary>Estimates execution latency with P50/P95 breakdowns.</summary>
    global::System.Threading.Tasks.Task<WorkflowLatencyEstimate> EstimateLatencyAsync(
        Guid workflowGraphId, CancellationToken ct = default);

    /// <summary>Records historical execution data for calibrating future predictions.</summary>
    global::System.Threading.Tasks.Task RecordHistoricalExecutionAsync(
        Guid workflowGraphId, bool isSuccess, double latencyMs, double cost,
        CancellationToken ct = default);

    /// <summary>Retrieves historical data for a workflow.</summary>
    global::System.Threading.Tasks.Task<WorkflowHistoricalData?> GetHistoricalDataAsync(
        Guid workflowGraphId, CancellationToken ct = default);
}

using ArchonAI.Core.Models.Workflow;
using ArchonAI.Core.Models.WorkflowSimulation;
using ArchonAI.Core.Models.StrategyLibrary;

namespace ArchonAI.Core.Interfaces;

/// <summary>
/// Low-level engine that computes simulation predictions from a workflow graph,
/// optional historical calibration data, and optional strategy resource profiles.
/// </summary>
public interface IWorkflowSimulationEngine
{
    /// <summary>Runs a simulation producing a step-by-step trace with predictions.</summary>
    global::System.Threading.Tasks.Task<WorkflowSimulationRunResult> RunSimulationAsync(
        WorkflowGraph graph,
        WorkflowHistoricalData? historicalData,
        StrategyTemplate? strategy,
        IReadOnlyDictionary<string, string>? overrides,
        CancellationToken ct = default);

    /// <summary>Predicts per-node outcomes.</summary>
    global::System.Threading.Tasks.Task<WorkflowOutcomePrediction> PredictNodeOutcomesAsync(
        WorkflowGraph graph,
        WorkflowHistoricalData? historicalData,
        CancellationToken ct = default);

    /// <summary>Estimates resource consumption.</summary>
    global::System.Threading.Tasks.Task<WorkflowResourceEstimate> EstimateResourceUsageAsync(
        WorkflowGraph graph,
        StrategyTemplate? strategy,
        CancellationToken ct = default);

    /// <summary>Estimates latency with critical-path analysis.</summary>
    global::System.Threading.Tasks.Task<WorkflowLatencyEstimate> EstimateLatencyAsync(
        WorkflowGraph graph,
        WorkflowHistoricalData? historicalData,
        CancellationToken ct = default);
}

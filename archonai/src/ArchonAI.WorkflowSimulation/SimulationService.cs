using System.Collections.Concurrent;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.WorkflowSimulation;
using Microsoft.Extensions.Logging;

namespace ArchonAI.WorkflowSimulation;

/// <summary>
/// High-level service that coordinates the simulation engine with workflow
/// definitions from the designer, historical execution data, and the strategy
/// library for resource estimation.
/// </summary>
public sealed class SimulationService : IWorkflowSimulationService
{
    private readonly IWorkflowSimulationEngine _engine;
    private readonly IWorkflowDesignerService _designer;
    private readonly IStrategyLibraryService _strategyLibrary;
    private readonly IEventBus _eventBus;
    private readonly ILogger<SimulationService> _logger;

    private readonly ConcurrentDictionary<Guid, WorkflowHistoricalData> _historicalData = new();

    private long _simulations;
    private long _predictions;
    private long _resourceEstimates;
    private long _latencyEstimates;

    public SimulationService(
        IWorkflowSimulationEngine engine,
        IWorkflowDesignerService designer,
        IStrategyLibraryService strategyLibrary,
        IEventBus eventBus,
        ILogger<SimulationService> logger)
    {
        _engine = engine;
        _designer = designer;
        _strategyLibrary = strategyLibrary;
        _eventBus = eventBus;
        _logger = logger;
    }

    // ── Simulate ─────────────────────────────────────────────────────

    public async global::System.Threading.Tasks.Task<WorkflowSimulationRunResult> SimulateAsync(
        Guid workflowGraphId, Guid? strategyId = null,
        IReadOnlyDictionary<string, string>? historicalOverrides = null,
        CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("WorkflowSimulation.Simulate");
        Interlocked.Increment(ref _simulations);
        Telemetry.WorkflowSimulationRuns.Add(1);

        var graph = await _designer.GetWorkflowGraphAsync(workflowGraphId, ct)
            ?? throw new InvalidOperationException($"Workflow graph {workflowGraphId} not found");

        _historicalData.TryGetValue(workflowGraphId, out var historicalData);

        Core.Models.StrategyLibrary.StrategyTemplate? strategy = null;
        if (strategyId.HasValue)
        {
            strategy = await _strategyLibrary.GetStrategyAsync(strategyId.Value, ct)
                ?? throw new InvalidOperationException($"Strategy {strategyId.Value} not found");
        }

        var result = await _engine.RunSimulationAsync(graph, historicalData, strategy, historicalOverrides, ct);

        _logger.LogInformation(
            "Simulation {SimulationId} for graph {GraphId}: success={Success}, latency={Latency}ms",
            result.SimulationId, workflowGraphId, result.PredictedSuccess, result.EstimatedLatencyMs);

        await EmitEventAsync("workflowsimulation.completed", result.SimulationId.ToString(),
            $"Graph '{graph.Name}' simulation: success={result.PredictedSuccess}, latency={result.EstimatedLatencyMs}ms");

        return result;
    }

    // ── Predict outcomes ─────────────────────────────────────────────

    public async global::System.Threading.Tasks.Task<WorkflowOutcomePrediction> PredictOutcomesAsync(
        Guid workflowGraphId, CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("WorkflowSimulation.PredictOutcomes");
        Interlocked.Increment(ref _predictions);
        Telemetry.WorkflowSimulationPredictions.Add(1);

        var graph = await _designer.GetWorkflowGraphAsync(workflowGraphId, ct)
            ?? throw new InvalidOperationException($"Workflow graph {workflowGraphId} not found");

        _historicalData.TryGetValue(workflowGraphId, out var historicalData);

        return await _engine.PredictNodeOutcomesAsync(graph, historicalData, ct);
    }

    // ── Resource estimation ──────────────────────────────────────────

    public async global::System.Threading.Tasks.Task<WorkflowResourceEstimate> EstimateResourcesAsync(
        Guid workflowGraphId, Guid? strategyId = null,
        CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("WorkflowSimulation.EstimateResources");
        Interlocked.Increment(ref _resourceEstimates);
        Telemetry.WorkflowSimulationResourceEstimates.Add(1);

        var graph = await _designer.GetWorkflowGraphAsync(workflowGraphId, ct)
            ?? throw new InvalidOperationException($"Workflow graph {workflowGraphId} not found");

        Core.Models.StrategyLibrary.StrategyTemplate? strategy = null;
        if (strategyId.HasValue)
        {
            strategy = await _strategyLibrary.GetStrategyAsync(strategyId.Value, ct);
        }

        return await _engine.EstimateResourceUsageAsync(graph, strategy, ct);
    }

    // ── Latency estimation ───────────────────────────────────────────

    public async global::System.Threading.Tasks.Task<WorkflowLatencyEstimate> EstimateLatencyAsync(
        Guid workflowGraphId, CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("WorkflowSimulation.EstimateLatency");
        Interlocked.Increment(ref _latencyEstimates);
        Telemetry.WorkflowSimulationLatencyEstimates.Add(1);

        var graph = await _designer.GetWorkflowGraphAsync(workflowGraphId, ct)
            ?? throw new InvalidOperationException($"Workflow graph {workflowGraphId} not found");

        _historicalData.TryGetValue(workflowGraphId, out var historicalData);

        return await _engine.EstimateLatencyAsync(graph, historicalData, ct);
    }

    // ── Historical data ──────────────────────────────────────────────

    public global::System.Threading.Tasks.Task RecordHistoricalExecutionAsync(
        Guid workflowGraphId, bool isSuccess, double latencyMs, double cost,
        CancellationToken ct = default)
    {
        _historicalData.AddOrUpdate(
            workflowGraphId,
            _ => new WorkflowHistoricalData(
                WorkflowGraphId: workflowGraphId,
                TotalExecutions: 1,
                HistoricalSuccessRate: isSuccess ? 1.0 : 0.0,
                HistoricalAverageLatencyMs: Math.Max(0, latencyMs),
                HistoricalP95LatencyMs: Math.Max(0, latencyMs),
                HistoricalAverageCost: Math.Max(0, cost),
                LastExecutionAtUtc: DateTimeOffset.UtcNow),
            (_, existing) =>
            {
                long newTotal = existing.TotalExecutions + 1;
                double newSuccessRate = ((existing.HistoricalSuccessRate * existing.TotalExecutions)
                    + (isSuccess ? 1.0 : 0.0)) / newTotal;
                double newAvgLatency = ((existing.HistoricalAverageLatencyMs * existing.TotalExecutions)
                    + Math.Max(0, latencyMs)) / newTotal;
                double newAvgCost = ((existing.HistoricalAverageCost * existing.TotalExecutions)
                    + Math.Max(0, cost)) / newTotal;
                // Approximate P95 using exponential moving max
                double newP95 = Math.Max(existing.HistoricalP95LatencyMs * 0.95, latencyMs);

                return existing with
                {
                    TotalExecutions = newTotal,
                    HistoricalSuccessRate = newSuccessRate,
                    HistoricalAverageLatencyMs = newAvgLatency,
                    HistoricalP95LatencyMs = newP95,
                    HistoricalAverageCost = newAvgCost,
                    LastExecutionAtUtc = DateTimeOffset.UtcNow
                };
            });

        _logger.LogDebug(
            "Historical execution recorded for graph {GraphId}: success={IsSuccess}",
            workflowGraphId, isSuccess);

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task<WorkflowHistoricalData?> GetHistoricalDataAsync(
        Guid workflowGraphId, CancellationToken ct = default)
    {
        _historicalData.TryGetValue(workflowGraphId, out var data);
        return global::System.Threading.Tasks.Task.FromResult(data);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private async global::System.Threading.Tasks.Task EmitEventAsync(
        string eventType, string source, string detail)
    {
        try
        {
            var payload = new Dictionary<string, string> { ["detail"] = detail };
            var evt = new Core.Models.SystemEvent(
                Id: Guid.NewGuid(),
                EventType: eventType,
                Source: source,
                CorrelationId: Guid.NewGuid(),
                Payload: payload,
                OccurredAtUtc: DateTimeOffset.UtcNow);
            await _eventBus.PublishAsync(evt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit event {EventType}", eventType);
        }
    }
}

using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.StrategyLibrary;
using Microsoft.Extensions.Logging;

namespace ArchonAI.StrategyLibrary;

/// <summary>
/// Service for managing the strategy library — storing templates, recording
/// executions, computing rankings, and comparing strategy performance.
/// </summary>
public sealed class StrategyService : IStrategyLibraryService
{
    private readonly IStrategyLibraryRepository _repository;
    private readonly IEventBus _eventBus;
    private readonly ILogger<StrategyService> _logger;

    private long _strategiesCreated;
    private long _executionsRecorded;
    private long _comparisons;

    public StrategyService(
        IStrategyLibraryRepository repository,
        IEventBus eventBus,
        ILogger<StrategyService> logger)
    {
        _repository = repository;
        _eventBus = eventBus;
        _logger = logger;
    }

    // ── Store ────────────────────────────────────────────────────────

    public async global::System.Threading.Tasks.Task<StrategyTemplate> CreateStrategyAsync(
        string name, string description, string objectiveType,
        string workflowTemplate, IReadOnlyDictionary<string, string> successMetrics,
        StrategyResourceUsage resourceUsage, IReadOnlyList<string>? tags = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("StrategyLibrary.Create");
        Interlocked.Increment(ref _strategiesCreated);
        Telemetry.StrategyLibraryCreated.Add(1);

        var now = DateTimeOffset.UtcNow;
        var strategy = new StrategyTemplate(
            Id: Guid.NewGuid(),
            Name: name,
            Description: description,
            ObjectiveType: objectiveType,
            WorkflowTemplate: workflowTemplate,
            SuccessMetrics: successMetrics,
            ResourceUsage: resourceUsage,
            Rank: new StrategyRank(
                SuccessRate: 0, AverageLatencyMs: 0, AverageCost: 0,
                TotalExecutions: 0, Score: 0, RankedAtUtc: now),
            Tags: tags ?? Array.Empty<string>(),
            Metadata: metadata ?? new Dictionary<string, string>(),
            CreatedAtUtc: now,
            UpdatedAtUtc: now);

        await _repository.UpsertStrategyAsync(strategy, ct);

        _logger.LogInformation(
            "Strategy created: {StrategyId} '{Name}' for objective '{ObjectiveType}'",
            strategy.Id, name, objectiveType);

        await EmitEventAsync("strategylibrary.strategy.created", strategy.Id.ToString(),
            $"Strategy '{name}' created for objective '{objectiveType}'");

        return strategy;
    }

    public async global::System.Threading.Tasks.Task<StrategyTemplate> UpdateStrategyAsync(
        Guid strategyId, string? description = null,
        string? workflowTemplate = null,
        IReadOnlyDictionary<string, string>? successMetrics = null,
        StrategyResourceUsage? resourceUsage = null,
        IReadOnlyList<string>? tags = null,
        CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("StrategyLibrary.Update");

        var existing = await _repository.GetStrategyAsync(strategyId, ct)
            ?? throw new InvalidOperationException($"Strategy {strategyId} not found");

        var updated = existing with
        {
            Description = description ?? existing.Description,
            WorkflowTemplate = workflowTemplate ?? existing.WorkflowTemplate,
            SuccessMetrics = successMetrics ?? existing.SuccessMetrics,
            ResourceUsage = resourceUsage ?? existing.ResourceUsage,
            Tags = tags ?? existing.Tags,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        await _repository.UpsertStrategyAsync(updated, ct);

        _logger.LogInformation("Strategy updated: {StrategyId} '{Name}'", strategyId, updated.Name);
        return updated;
    }

    public async global::System.Threading.Tasks.Task<StrategyTemplate?> GetStrategyAsync(
        Guid strategyId, CancellationToken ct = default)
    {
        return await _repository.GetStrategyAsync(strategyId, ct);
    }

    public async global::System.Threading.Tasks.Task DeleteStrategyAsync(
        Guid strategyId, CancellationToken ct = default)
    {
        var existing = await _repository.GetStrategyAsync(strategyId, ct)
            ?? throw new InvalidOperationException($"Strategy {strategyId} not found");

        await _repository.RemoveStrategyAsync(strategyId, ct);

        _logger.LogInformation("Strategy deleted: {StrategyId} '{Name}'", strategyId, existing.Name);
        await EmitEventAsync("strategylibrary.strategy.deleted", strategyId.ToString(), existing.Name);
    }

    // ── Retrieve ─────────────────────────────────────────────────────

    public async global::System.Threading.Tasks.Task<IReadOnlyList<StrategyTemplate>> ListStrategiesAsync(
        string? objectiveType = null, string? tag = null,
        int offset = 0, int limit = 50,
        CancellationToken ct = default)
    {
        return await _repository.ListStrategiesAsync(objectiveType, tag, offset, limit, ct);
    }

    // ── Rank ─────────────────────────────────────────────────────────

    public async global::System.Threading.Tasks.Task<StrategyExecutionRecord> RecordExecutionAsync(
        Guid strategyId, bool isSuccess, double latencyMs, double cost,
        IReadOnlyDictionary<string, string>? outcomes = null,
        CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("StrategyLibrary.RecordExecution");
        Interlocked.Increment(ref _executionsRecorded);
        Telemetry.StrategyLibraryExecutionsRecorded.Add(1);

        var strategy = await _repository.GetStrategyAsync(strategyId, ct)
            ?? throw new InvalidOperationException($"Strategy {strategyId} not found");

        var record = new StrategyExecutionRecord(
            Id: Guid.NewGuid(),
            StrategyId: strategyId,
            IsSuccess: isSuccess,
            LatencyMs: Math.Max(0, latencyMs),
            Cost: Math.Max(0, cost),
            Outcomes: outcomes ?? new Dictionary<string, string>(),
            ExecutedAtUtc: DateTimeOffset.UtcNow);

        await _repository.AddExecutionRecordAsync(record, ct);

        // Recompute rank from execution history
        var executions = await _repository.GetExecutionRecordsAsync(strategyId, 1_000, ct);
        var newRank = ComputeRank(executions);

        var ranked = strategy with { Rank = newRank, UpdatedAtUtc = DateTimeOffset.UtcNow };
        await _repository.UpsertStrategyAsync(ranked, ct);

        _logger.LogDebug(
            "Execution recorded for strategy {StrategyId}: success={IsSuccess}, score={Score:F2}",
            strategyId, isSuccess, newRank.Score);

        return record;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<StrategyTemplate>> GetRankedStrategiesAsync(
        string objectiveType, int limit = 10,
        CancellationToken ct = default)
    {
        Telemetry.StrategyLibraryRankQueries.Add(1);

        var all = await _repository.ListStrategiesAsync(objectiveType, null, 0, limit, ct);
        // Already sorted by score in repository
        return all;
    }

    // ── Compare ──────────────────────────────────────────────────────

    public async global::System.Threading.Tasks.Task<StrategyComparisonResult> CompareStrategiesAsync(
        IReadOnlyList<Guid> strategyIds, CancellationToken ct = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("StrategyLibrary.Compare");
        Interlocked.Increment(ref _comparisons);
        Telemetry.StrategyLibraryComparisons.Add(1);

        if (strategyIds.Count < 2)
            throw new InvalidOperationException("At least two strategies are required for comparison");

        var entries = new List<StrategyComparisonEntry>();
        foreach (var id in strategyIds)
        {
            var strategy = await _repository.GetStrategyAsync(id, ct)
                ?? throw new InvalidOperationException($"Strategy {id} not found");

            entries.Add(new StrategyComparisonEntry(
                StrategyId: strategy.Id,
                Name: strategy.Name,
                ObjectiveType: strategy.ObjectiveType,
                Rank: strategy.Rank,
                ResourceUsage: strategy.ResourceUsage));
        }

        // Recommend the strategy with the highest score
        var best = entries.OrderByDescending(e => e.Rank.Score).First();
        string reason = best.Rank.TotalExecutions > 0
            ? $"Highest score ({best.Rank.Score:F2}) with {best.Rank.SuccessRate:P0} success rate over {best.Rank.TotalExecutions} executions"
            : $"Highest score ({best.Rank.Score:F2}); no executions recorded yet";

        _logger.LogInformation(
            "Strategy comparison: {Count} strategies compared, recommended={RecommendedId}",
            strategyIds.Count, best.StrategyId);

        return new StrategyComparisonResult(
            Entries: entries,
            RecommendedStrategyId: best.StrategyId,
            RecommendationReason: reason,
            ComparedAtUtc: DateTimeOffset.UtcNow);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static StrategyRank ComputeRank(IReadOnlyList<StrategyExecutionRecord> executions)
    {
        if (executions.Count == 0)
            return new StrategyRank(0, 0, 0, 0, 0, DateTimeOffset.UtcNow);

        long total = executions.Count;
        long successes = executions.Count(e => e.IsSuccess);
        double successRate = (double)successes / total;
        double avgLatency = executions.Average(e => e.LatencyMs);
        double avgCost = executions.Average(e => e.Cost);

        // Score: weighted combination — success rate dominates, lower latency and cost are better
        double latencyPenalty = avgLatency > 0 ? Math.Min(1.0, 1000.0 / avgLatency) : 1.0;
        double costPenalty = avgCost > 0 ? Math.Min(1.0, 10.0 / avgCost) : 1.0;
        double score = (successRate * 0.6) + (latencyPenalty * 0.25) + (costPenalty * 0.15);

        return new StrategyRank(
            SuccessRate: successRate,
            AverageLatencyMs: avgLatency,
            AverageCost: avgCost,
            TotalExecutions: total,
            Score: Math.Round(score, 4),
            RankedAtUtc: DateTimeOffset.UtcNow);
    }

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

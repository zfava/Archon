using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.StrategyLibrary;
using Microsoft.Extensions.Logging;

namespace ArchonAI.StrategyLibrary;

/// <summary>
/// In-memory implementation of <see cref="IStrategyLibraryRepository"/>.
/// </summary>
public sealed class StrategyRepository : IStrategyLibraryRepository
{
    private readonly ConcurrentDictionary<Guid, StrategyTemplate> _strategies = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<StrategyExecutionRecord>> _executions = new();
    private const int MaxExecutionsPerStrategy = 1_000;

    private readonly ILogger<StrategyRepository> _logger;

    public StrategyRepository(ILogger<StrategyRepository> logger)
    {
        _logger = logger;
    }

    // ── Templates ────────────────────────────────────────────────────

    public global::System.Threading.Tasks.Task<StrategyTemplate> UpsertStrategyAsync(
        StrategyTemplate strategy, CancellationToken ct = default)
    {
        _strategies[strategy.Id] = strategy;
        _logger.LogDebug("Upserted strategy {StrategyId} '{Name}'", strategy.Id, strategy.Name);
        return global::System.Threading.Tasks.Task.FromResult(strategy);
    }

    public global::System.Threading.Tasks.Task<StrategyTemplate?> GetStrategyAsync(
        Guid strategyId, CancellationToken ct = default)
    {
        _strategies.TryGetValue(strategyId, out var strategy);
        return global::System.Threading.Tasks.Task.FromResult(strategy);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<StrategyTemplate>> ListStrategiesAsync(
        string? objectiveType, string? tag,
        int offset, int limit, CancellationToken ct = default)
    {
        IEnumerable<StrategyTemplate> query = _strategies.Values
            .OrderByDescending(s => s.Rank.Score)
            .ThenByDescending(s => s.UpdatedAtUtc);

        if (!string.IsNullOrWhiteSpace(objectiveType))
            query = query.Where(s =>
                s.ObjectiveType.Equals(objectiveType, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(tag))
            query = query.Where(s =>
                s.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)));

        IReadOnlyList<StrategyTemplate> result = query.Skip(offset).Take(limit).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public global::System.Threading.Tasks.Task<bool> RemoveStrategyAsync(
        Guid strategyId, CancellationToken ct = default)
    {
        bool removed = _strategies.TryRemove(strategyId, out _);
        _executions.TryRemove(strategyId, out _);
        if (removed)
            _logger.LogDebug("Removed strategy {StrategyId}", strategyId);
        return global::System.Threading.Tasks.Task.FromResult(removed);
    }

    // ── Execution records ────────────────────────────────────────────

    public global::System.Threading.Tasks.Task AddExecutionRecordAsync(
        StrategyExecutionRecord record, CancellationToken ct = default)
    {
        var queue = _executions.GetOrAdd(record.StrategyId, _ => new ConcurrentQueue<StrategyExecutionRecord>());
        queue.Enqueue(record);
        while (queue.Count > MaxExecutionsPerStrategy) queue.TryDequeue(out _);
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<StrategyExecutionRecord>> GetExecutionRecordsAsync(
        Guid strategyId, int limit = 100, CancellationToken ct = default)
    {
        if (!_executions.TryGetValue(strategyId, out var queue))
            return global::System.Threading.Tasks.Task.FromResult<IReadOnlyList<StrategyExecutionRecord>>(
                Array.Empty<StrategyExecutionRecord>());

        IReadOnlyList<StrategyExecutionRecord> result = queue
            .OrderByDescending(r => r.ExecutedAtUtc)
            .Take(limit)
            .ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }
}

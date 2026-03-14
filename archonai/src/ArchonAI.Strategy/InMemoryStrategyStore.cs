using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Planning;
using Microsoft.Extensions.Options;

namespace ArchonAI.Strategy;

public sealed class InMemoryStrategyStore : IStrategyStore
{
    private readonly ConcurrentDictionary<Guid, OperationalStrategy> _strategies = new();
    private readonly IMemoryStore _memoryStore;

    public InMemoryStrategyStore(IOptions<StrategyOptions> options, IMemoryStore memoryStore)
    {
        _memoryStore = memoryStore;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (StrategySeed seed in options.Value.Seeds)
        {
            var strategy = new OperationalStrategy(
                Id: Guid.NewGuid(),
                ObjectiveType: seed.ObjectiveType,
                WorkflowTemplate: seed.WorkflowTemplate,
                RecommendedAgents: seed.RecommendedAgents,
                SuccessMetrics: seed.SuccessMetrics,
                CreatedAtUtc: now,
                UpdatedAtUtc: now);

            _strategies[strategy.Id] = strategy;
        }
    }

    public async global::System.Threading.Tasks.Task SaveAsync(OperationalStrategy strategy, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = strategy with
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = strategy.CreatedAtUtc == default ? DateTimeOffset.UtcNow : strategy.CreatedAtUtc
        };

        _strategies[normalized.Id] = normalized;

        var memoryRecord = new MemoryRecord(
            Id: Guid.NewGuid(),
            MemoryType: "strategy",
            Scope: $"strategy:{normalized.ObjectiveType}",
            Content: normalized.WorkflowTemplate,
            Metadata: new Dictionary<string, string>(normalized.SuccessMetrics)
            {
                ["strategyId"] = normalized.Id.ToString(),
                ["objectiveType"] = normalized.ObjectiveType,
                ["recommendedAgents"] = string.Join(',', normalized.RecommendedAgents)
            },
            CreatedAtUtc: normalized.UpdatedAtUtc,
            ExpiresAtUtc: null);

        await _memoryStore.SaveAsync(memoryRecord, cancellationToken);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<OperationalStrategy>> QueryByObjectiveTypeAsync(
        string objectiveType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<OperationalStrategy> results = _strategies.Values
            .Where(strategy => strategy.ObjectiveType.Equals(objectiveType, StringComparison.OrdinalIgnoreCase)
                            || strategy.ObjectiveType.Equals("default", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(strategy => ParseScore(strategy.SuccessMetrics, "successRate"))
            .ThenByDescending(strategy => strategy.UpdatedAtUtc)
            .ToArray();

        return global::System.Threading.Tasks.Task.FromResult(results);
    }

    private static double ParseScore(IReadOnlyDictionary<string, string> metrics, string key)
    {
        return metrics.TryGetValue(key, out string? value) && double.TryParse(value, out double parsed)
            ? parsed
            : 0;
    }
}

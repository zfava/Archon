using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Planning;

namespace ArchonAI.MultiTenant;

public sealed class TenantStrategyStore : IStrategyStore
{
    private readonly IMultiTenantContext _tenantContext;
    private readonly ConcurrentDictionary<string, OperationalStrategy> _tenantStrategies = new(StringComparer.OrdinalIgnoreCase);

    public TenantStrategyStore(IMultiTenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public global::System.Threading.Tasks.Task SaveAsync(OperationalStrategy strategy, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string tenant = _tenantContext.CurrentTenantId;
        string key = BuildKey(tenant, strategy.ObjectiveType, strategy.Id);

        var normalized = strategy with
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = strategy.CreatedAtUtc == default ? DateTimeOffset.UtcNow : strategy.CreatedAtUtc
        };

        _tenantStrategies[key] = normalized;
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<OperationalStrategy>> QueryByObjectiveTypeAsync(string objectiveType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string tenant = _tenantContext.CurrentTenantId;
        IReadOnlyList<OperationalStrategy> results = _tenantStrategies
            .Where(kv => kv.Key.StartsWith($"{tenant}::", StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Value)
            .Where(strategy => strategy.ObjectiveType.Equals(objectiveType, StringComparison.OrdinalIgnoreCase)
                            || strategy.ObjectiveType.Equals("default", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(strategy => strategy.UpdatedAtUtc)
            .ToArray();

        return global::System.Threading.Tasks.Task.FromResult(results);
    }

    private static string BuildKey(string tenantId, string objectiveType, Guid strategyId)
        => $"{tenantId}::{objectiveType}::{strategyId}";
}

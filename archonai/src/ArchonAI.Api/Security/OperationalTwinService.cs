using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.OperationalTwin;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Api.Security;

public sealed class OperationalTwinService : IOperationalTwinService
{
    private readonly ConcurrentDictionary<Guid, TwinEntity> _entities = new();
    private readonly ConcurrentDictionary<Guid, TwinDependency> _dependencies = new();
    private readonly ConcurrentDictionary<string, TwinKpi> _kpis = new(); // key: entityId+metricName
    private readonly ConcurrentDictionary<Guid, TwinBottleneck> _bottlenecks = new();
    private readonly ConcurrentDictionary<Guid, TwinArtifactLink> _artifactLinks = new();
    private readonly IEventBus _eventBus;
    private readonly ILogger<OperationalTwinService> _logger;

    public OperationalTwinService(IEventBus eventBus, ILogger<OperationalTwinService> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    // ── Entities ──────────────────────────────────────────────

    public async Task<TwinEntity> UpsertEntityAsync(TwinEntity entity, CancellationToken ct = default)
    {
        _entities[entity.Id] = entity;

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(), "twin.entity.upserted", "OperationalTwinService",
            entity.Id,
            new Dictionary<string, string>
            {
                ["entityId"] = entity.Id.ToString(),
                ["tenantId"] = entity.TenantId.ToString(),
                ["type"] = entity.EntityType.ToString(),
                ["name"] = entity.Name,
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation("Twin entity upserted: {EntityId} type={Type} name={Name}",
            entity.Id, entity.EntityType, entity.Name);
        return entity;
    }

    public Task<TwinEntity?> GetEntityAsync(Guid entityId, Guid tenantId, CancellationToken ct = default)
    {
        _entities.TryGetValue(entityId, out var entity);
        if (entity is not null && entity.TenantId != tenantId)
            return Task.FromResult<TwinEntity?>(null);
        return Task.FromResult(entity);
    }

    public Task<IReadOnlyList<TwinEntity>> ListEntitiesAsync(
        Guid tenantId, TwinEntityType? type = null, CancellationToken ct = default)
    {
        var q = _entities.Values.Where(e => e.TenantId == tenantId);
        if (type.HasValue) q = q.Where(e => e.EntityType == type.Value);
        IReadOnlyList<TwinEntity> result = q.OrderBy(e => e.EntityType).ThenBy(e => e.Name).ToList();
        return Task.FromResult(result);
    }

    public Task<bool> DeleteEntityAsync(Guid entityId, Guid tenantId, CancellationToken ct = default)
    {
        if (!_entities.TryGetValue(entityId, out var e) || e.TenantId != tenantId)
            return Task.FromResult(false);
        return Task.FromResult(_entities.TryRemove(entityId, out _));
    }

    // ── Dependencies ──────────────────────────────────────────

    public Task<TwinDependency> AddDependencyAsync(TwinDependency dep, CancellationToken ct = default)
    {
        _dependencies[dep.Id] = dep;
        return Task.FromResult(dep);
    }

    public Task<IReadOnlyList<TwinDependency>> GetDependenciesAsync(
        Guid entityId, Guid tenantId, CancellationToken ct = default)
    {
        IReadOnlyList<TwinDependency> result = _dependencies.Values
            .Where(d => d.TenantId == tenantId &&
                        (d.FromEntityId == entityId || d.ToEntityId == entityId))
            .OrderByDescending(d => d.CriticalityScore ?? 0)
            .ToList();
        return Task.FromResult(result);
    }

    // ── KPIs ──────────────────────────────────────────────────

    public Task<TwinKpi> RecordKpiAsync(TwinKpi kpi, CancellationToken ct = default)
    {
        var key = $"{kpi.EntityId}:{kpi.MetricName}";
        _kpis[key] = kpi;
        return Task.FromResult(kpi);
    }

    public Task<IReadOnlyList<TwinKpi>> GetKpisAsync(Guid entityId, CancellationToken ct = default)
    {
        IReadOnlyList<TwinKpi> result = _kpis.Values
            .Where(k => k.EntityId == entityId)
            .OrderBy(k => k.MetricName)
            .ToList();
        return Task.FromResult(result);
    }

    // ── Bottlenecks ───────────────────────────────────────────

    public Task<TwinBottleneck> ReportBottleneckAsync(TwinBottleneck bottleneck, CancellationToken ct = default)
    {
        _bottlenecks[bottleneck.Id] = bottleneck;
        _logger.LogWarning("Bottleneck reported: {Id} severity={Severity} entity={Entity}",
            bottleneck.Id, bottleneck.Severity, bottleneck.AffectedEntityId);
        return Task.FromResult(bottleneck);
    }

    public Task<TwinBottleneck?> ResolveBottleneckAsync(
        Guid bottleneckId, Guid tenantId, CancellationToken ct = default)
    {
        if (!_bottlenecks.TryGetValue(bottleneckId, out var bn) || bn.TenantId != tenantId)
            return Task.FromResult<TwinBottleneck?>(null);

        var resolved = bn with { IsResolved = true, ResolvedAtUtc = DateTimeOffset.UtcNow };
        _bottlenecks[bottleneckId] = resolved;
        return Task.FromResult<TwinBottleneck?>(resolved);
    }

    public Task<IReadOnlyList<TwinBottleneck>> ListBottlenecksAsync(
        Guid tenantId, bool activeOnly = true, CancellationToken ct = default)
    {
        var q = _bottlenecks.Values.Where(b => b.TenantId == tenantId);
        if (activeOnly) q = q.Where(b => !b.IsResolved);
        IReadOnlyList<TwinBottleneck> result = q
            .OrderByDescending(b => b.Severity)
            .ThenByDescending(b => b.DetectedAtUtc)
            .ToList();
        return Task.FromResult(result);
    }

    // ── Artifact Links ────────────────────────────────────────

    public Task<TwinArtifactLink> LinkArtifactAsync(TwinArtifactLink link, CancellationToken ct = default)
    {
        _artifactLinks[link.Id] = link;
        return Task.FromResult(link);
    }

    public Task<IReadOnlyList<TwinArtifactLink>> GetArtifactLinksAsync(
        Guid entityId, CancellationToken ct = default)
    {
        IReadOnlyList<TwinArtifactLink> result = _artifactLinks.Values
            .Where(l => l.TwinEntityId == entityId)
            .OrderByDescending(l => l.LinkedAtUtc)
            .ToList();
        return Task.FromResult(result);
    }

    // ── Overview ──────────────────────────────────────────────

    public Task<TwinOverview> GetOverviewAsync(Guid tenantId, CancellationToken ct = default)
    {
        var entities = _entities.Values.Where(e => e.TenantId == tenantId).ToList();
        var entityCounts = entities
            .GroupBy(e => e.EntityType.ToString())
            .ToDictionary(g => g.Key, g => g.Count())
            as IReadOnlyDictionary<string, int>;

        var activeBottlenecks = _bottlenecks.Values
            .Where(b => b.TenantId == tenantId && !b.IsResolved)
            .OrderByDescending(b => b.Severity)
            .ToList();

        // KPIs in warning/critical state
        var entityIds = new HashSet<Guid>(entities.Select(e => e.Id));
        var warningKpis = _kpis.Values
            .Where(k => entityIds.Contains(k.EntityId))
            .Where(IsKpiInWarning)
            .ToList();

        var depCount = _dependencies.Values.Count(d => d.TenantId == tenantId);

        return Task.FromResult(new TwinOverview(
            tenantId, entityCounts, activeBottlenecks,
            warningKpis, depCount, DateTimeOffset.UtcNow));
    }

    private static bool IsKpiInWarning(TwinKpi kpi)
    {
        if (kpi.ThresholdWarning is null && kpi.ThresholdCritical is null)
            return false;

        if (kpi.Direction == KpiDirection.HigherIsBetter)
        {
            if (kpi.ThresholdCritical.HasValue && kpi.CurrentValue <= kpi.ThresholdCritical.Value)
                return true;
            if (kpi.ThresholdWarning.HasValue && kpi.CurrentValue <= kpi.ThresholdWarning.Value)
                return true;
        }
        else
        {
            if (kpi.ThresholdCritical.HasValue && kpi.CurrentValue >= kpi.ThresholdCritical.Value)
                return true;
            if (kpi.ThresholdWarning.HasValue && kpi.CurrentValue >= kpi.ThresholdWarning.Value)
                return true;
        }
        return false;
    }
}

using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Memory;
using Task = System.Threading.Tasks.Task;

namespace ArchonAI.Api.Security;

public sealed class EnterpriseMemoryService : IEnterpriseMemoryService
{
    private readonly ConcurrentDictionary<Guid, EnterpriseMemoryRecord> _records = new();
    private readonly IEventBus _eventBus;
    private readonly ILogger<EnterpriseMemoryService> _logger;

    /// <summary>Default TTL for session-layer memories when no explicit expiry is set.</summary>
    private static readonly TimeSpan SessionDefaultTtl = TimeSpan.FromHours(4);

    public EnterpriseMemoryService(IEventBus eventBus, ILogger<EnterpriseMemoryService> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<EnterpriseMemoryRecord> StoreAsync(
        EnterpriseMemoryRecord record, CancellationToken ct = default)
    {
        // Enforce session-layer TTL if caller didn't set one
        var stored = record.Layer == MemoryLayer.Session && record.ExpiresAtUtc is null
            ? record with { ExpiresAtUtc = DateTimeOffset.UtcNow.Add(SessionDefaultTtl) }
            : record;

        _records[stored.Id] = stored;

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(), $"memory.{stored.Layer.ToString().ToLowerInvariant()}.stored",
            "EnterpriseMemoryService", stored.Id,
            new Dictionary<string, string>
            {
                ["recordId"] = stored.Id.ToString(),
                ["tenantId"] = stored.TenantId.ToString(),
                ["layer"] = stored.Layer.ToString(),
                ["category"] = stored.Category,
                ["subject"] = stored.Subject,
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);

        _logger.LogInformation(
            "Memory stored: {RecordId} layer={Layer} category={Category} tenant={TenantId}",
            stored.Id, stored.Layer, stored.Category, stored.TenantId);

        return stored;
    }

    public Task<EnterpriseMemoryRecord?> GetAsync(
        Guid recordId, Guid tenantId, CancellationToken ct = default)
    {
        _records.TryGetValue(recordId, out var record);
        if (record is not null && record.TenantId != tenantId)
            return Task.FromResult<EnterpriseMemoryRecord?>(null);
        if (record is not null && IsExpired(record))
            return Task.FromResult<EnterpriseMemoryRecord?>(null);
        return Task.FromResult(record);
    }

    public Task<EnterpriseMemoryQueryResult> QueryAsync(
        Guid tenantId, MemoryLayer? layer = null, string? category = null,
        string? tag = null, int limit = 50, CancellationToken ct = default)
    {
        var query = _records.Values
            .Where(r => r.TenantId == tenantId)
            .Where(r => !IsExpired(r));

        if (layer.HasValue)
            query = query.Where(r => r.Layer == layer.Value);
        if (category is not null)
            query = query.Where(r => string.Equals(r.Category, category, StringComparison.OrdinalIgnoreCase));
        if (tag is not null)
            query = query.Where(r => r.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));

        var all = query.OrderByDescending(r => r.CreatedAtUtc).ToList();

        var layerCounts = all
            .GroupBy(r => r.Layer.ToString())
            .ToDictionary(g => g.Key, g => g.Count())
            as IReadOnlyDictionary<string, int>;

        IReadOnlyList<EnterpriseMemoryRecord> records = all.Take(limit).ToList();

        return Task.FromResult(new EnterpriseMemoryQueryResult(records, all.Count, layerCounts));
    }

    public Task<EntityMemoryView> GetEntityMemoryAsync(
        Guid tenantId, string entityType, string entityId,
        CancellationToken ct = default)
    {
        var linked = _records.Values
            .Where(r => r.TenantId == tenantId)
            .Where(r => !IsExpired(r))
            .Where(r => r.LinkedEntities.Any(e =>
                string.Equals(e.EntityType, entityType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.EntityId, entityId, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToList();

        var layerDist = linked
            .GroupBy(r => r.Layer.ToString())
            .ToDictionary(g => g.Key, g => g.Count())
            as IReadOnlyDictionary<string, int>;

        return Task.FromResult(new EntityMemoryView(entityType, entityId, linked, layerDist));
    }

    public Task<IReadOnlyList<EnterpriseMemoryRecord>> GetTimelineAsync(
        Guid tenantId, MemoryLayer? layer = null, int limit = 100,
        CancellationToken ct = default)
    {
        var query = _records.Values
            .Where(r => r.TenantId == tenantId)
            .Where(r => !IsExpired(r));

        if (layer.HasValue)
            query = query.Where(r => r.Layer == layer.Value);

        IReadOnlyList<EnterpriseMemoryRecord> result = query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(limit)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<bool> DeleteAsync(
        Guid recordId, Guid tenantId, CancellationToken ct = default)
    {
        if (!_records.TryGetValue(recordId, out var record))
            return Task.FromResult(false);
        if (record.TenantId != tenantId)
            return Task.FromResult(false);
        return Task.FromResult(_records.TryRemove(recordId, out _));
    }

    public Task<int> ExpireSessionMemoryAsync(
        Guid tenantId, TimeSpan maxAge, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        var expired = _records.Values
            .Where(r => r.TenantId == tenantId
                        && r.Layer == MemoryLayer.Session
                        && r.CreatedAtUtc < cutoff)
            .ToList();

        var count = 0;
        foreach (var r in expired)
        {
            if (_records.TryRemove(r.Id, out _))
                count++;
        }

        if (count > 0)
            _logger.LogInformation("Expired {Count} session memories for tenant {TenantId}", count, tenantId);

        return Task.FromResult(count);
    }

    private static bool IsExpired(EnterpriseMemoryRecord r)
        => r.ExpiresAtUtc.HasValue && r.ExpiresAtUtc.Value <= DateTimeOffset.UtcNow;
}

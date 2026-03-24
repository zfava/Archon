using ArchonAI.Core.Models.Memory;

namespace ArchonAI.Core.Interfaces;

public interface IEnterpriseMemoryService
{
    /// <summary>Store a memory record in the appropriate layer.</summary>
    Task<EnterpriseMemoryRecord> StoreAsync(
        EnterpriseMemoryRecord record, CancellationToken ct = default);

    /// <summary>Retrieve a specific memory record.</summary>
    Task<EnterpriseMemoryRecord?> GetAsync(
        Guid recordId, Guid tenantId, CancellationToken ct = default);

    /// <summary>Query memory records by layer and/or category within a tenant.</summary>
    Task<EnterpriseMemoryQueryResult> QueryAsync(
        Guid tenantId, MemoryLayer? layer = null, string? category = null,
        string? tag = null, int limit = 50, CancellationToken ct = default);

    /// <summary>Get all memory linked to a specific entity (decision, workflow, customer, etc.).</summary>
    Task<EntityMemoryView> GetEntityMemoryAsync(
        Guid tenantId, string entityType, string entityId,
        CancellationToken ct = default);

    /// <summary>Get memory timeline for a tenant, ordered by creation time.</summary>
    Task<IReadOnlyList<EnterpriseMemoryRecord>> GetTimelineAsync(
        Guid tenantId, MemoryLayer? layer = null, int limit = 100,
        CancellationToken ct = default);

    /// <summary>Delete a memory record (tenant-scoped).</summary>
    Task<bool> DeleteAsync(
        Guid recordId, Guid tenantId, CancellationToken ct = default);

    /// <summary>Expire session-layer memories older than the given threshold.</summary>
    Task<int> ExpireSessionMemoryAsync(
        Guid tenantId, TimeSpan maxAge, CancellationToken ct = default);
}

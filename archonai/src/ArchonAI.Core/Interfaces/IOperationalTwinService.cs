using ArchonAI.Core.Models.OperationalTwin;

namespace ArchonAI.Core.Interfaces;

public interface IOperationalTwinService
{
    // ── Entities ──────────────────────────────────────────────
    Task<TwinEntity> UpsertEntityAsync(TwinEntity entity, CancellationToken ct = default);
    Task<TwinEntity?> GetEntityAsync(Guid entityId, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<TwinEntity>> ListEntitiesAsync(
        Guid tenantId, TwinEntityType? type = null, CancellationToken ct = default);
    Task<bool> DeleteEntityAsync(Guid entityId, Guid tenantId, CancellationToken ct = default);

    // ── Dependencies ──────────────────────────────────────────
    Task<TwinDependency> AddDependencyAsync(TwinDependency dep, CancellationToken ct = default);
    Task<IReadOnlyList<TwinDependency>> GetDependenciesAsync(
        Guid entityId, Guid tenantId, CancellationToken ct = default);

    // ── KPIs ──────────────────────────────────────────────────
    Task<TwinKpi> RecordKpiAsync(TwinKpi kpi, CancellationToken ct = default);
    Task<IReadOnlyList<TwinKpi>> GetKpisAsync(
        Guid entityId, CancellationToken ct = default);

    // ── Bottlenecks ───────────────────────────────────────────
    Task<TwinBottleneck> ReportBottleneckAsync(TwinBottleneck bottleneck, CancellationToken ct = default);
    Task<TwinBottleneck?> ResolveBottleneckAsync(
        Guid bottleneckId, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<TwinBottleneck>> ListBottlenecksAsync(
        Guid tenantId, bool activeOnly = true, CancellationToken ct = default);

    // ── Artifact Links ────────────────────────────────────────
    Task<TwinArtifactLink> LinkArtifactAsync(TwinArtifactLink link, CancellationToken ct = default);
    Task<IReadOnlyList<TwinArtifactLink>> GetArtifactLinksAsync(
        Guid entityId, CancellationToken ct = default);

    // ── Overview ──────────────────────────────────────────────
    Task<TwinOverview> GetOverviewAsync(Guid tenantId, CancellationToken ct = default);
}

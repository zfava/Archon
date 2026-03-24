using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.OperationalTwin;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Integration;

public sealed class OperationalTwinTests
{
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<OperationalTwinService> _logger = Substitute.For<ILogger<OperationalTwinService>>();

    private OperationalTwinService CreateService() => new(_eventBus, _logger);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();

    private TwinEntity MakeEntity(
        TwinEntityType type = TwinEntityType.Team,
        string name = "Engineering",
        TwinEntityStatus status = TwinEntityStatus.Active,
        Guid? tenantId = null,
        IReadOnlyList<string>? tags = null) =>
        new(
            Id: Guid.NewGuid(),
            TenantId: tenantId ?? _tenantId,
            EntityType: type,
            Name: name,
            Description: $"{name} description",
            Status: status,
            Properties: new Dictionary<string, string>().AsReadOnly(),
            Tags: tags ?? Array.Empty<string>(),
            CreatedBy: "test",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

    // ── Entity CRUD ────────────────────────────────────────────

    [Fact]
    public async Task UpsertEntity_StoresAndReturns()
    {
        var svc = CreateService();
        var entity = MakeEntity();

        var result = await svc.UpsertEntityAsync(entity);

        Assert.Equal(entity.Id, result.Id);
        Assert.Equal(entity.Name, result.Name);
    }

    [Fact]
    public async Task GetEntity_ReturnsStoredEntity()
    {
        var svc = CreateService();
        var entity = MakeEntity();
        await svc.UpsertEntityAsync(entity);

        var result = await svc.GetEntityAsync(entity.Id, _tenantId);

        Assert.NotNull(result);
        Assert.Equal(entity.Name, result!.Name);
    }

    [Fact]
    public async Task GetEntity_WrongTenant_ReturnsNull()
    {
        var svc = CreateService();
        var entity = MakeEntity();
        await svc.UpsertEntityAsync(entity);

        var result = await svc.GetEntityAsync(entity.Id, _otherTenantId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetEntity_NonExistent_ReturnsNull()
    {
        var svc = CreateService();

        var result = await svc.GetEntityAsync(Guid.NewGuid(), _tenantId);

        Assert.Null(result);
    }

    [Fact]
    public async Task ListEntities_FiltersByTenant()
    {
        var svc = CreateService();
        await svc.UpsertEntityAsync(MakeEntity(name: "A"));
        await svc.UpsertEntityAsync(MakeEntity(name: "B", tenantId: _otherTenantId));

        var results = await svc.ListEntitiesAsync(_tenantId);

        Assert.Single(results);
        Assert.Equal("A", results[0].Name);
    }

    [Fact]
    public async Task ListEntities_FiltersByType()
    {
        var svc = CreateService();
        await svc.UpsertEntityAsync(MakeEntity(type: TwinEntityType.Team, name: "Team A"));
        await svc.UpsertEntityAsync(MakeEntity(type: TwinEntityType.System, name: "System B"));

        var results = await svc.ListEntitiesAsync(_tenantId, TwinEntityType.System);

        Assert.Single(results);
        Assert.Equal("System B", results[0].Name);
    }

    [Fact]
    public async Task DeleteEntity_RemovesEntity()
    {
        var svc = CreateService();
        var entity = MakeEntity();
        await svc.UpsertEntityAsync(entity);

        var deleted = await svc.DeleteEntityAsync(entity.Id, _tenantId);

        Assert.True(deleted);
        Assert.Null(await svc.GetEntityAsync(entity.Id, _tenantId));
    }

    [Fact]
    public async Task DeleteEntity_WrongTenant_ReturnsFalse()
    {
        var svc = CreateService();
        var entity = MakeEntity();
        await svc.UpsertEntityAsync(entity);

        var deleted = await svc.DeleteEntityAsync(entity.Id, _otherTenantId);

        Assert.False(deleted);
        Assert.NotNull(await svc.GetEntityAsync(entity.Id, _tenantId));
    }

    [Fact]
    public async Task UpsertEntity_PublishesEvent()
    {
        var svc = CreateService();
        await svc.UpsertEntityAsync(MakeEntity());

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "twin.entity.upserted"),
            Arg.Any<CancellationToken>());
    }

    // ── Dependencies ───────────────────────────────────────────

    [Fact]
    public async Task AddDependency_StoresAndReturns()
    {
        var svc = CreateService();
        var dep = new TwinDependency(
            Guid.NewGuid(), _tenantId, Guid.NewGuid(), Guid.NewGuid(),
            DependencyType.DependsOn, "label", 0.8, DateTimeOffset.UtcNow);

        var result = await svc.AddDependencyAsync(dep);

        Assert.Equal(dep.Id, result.Id);
    }

    [Fact]
    public async Task GetDependencies_ReturnsMatchingEntityDeps()
    {
        var svc = CreateService();
        var entityA = Guid.NewGuid();
        var entityB = Guid.NewGuid();
        var entityC = Guid.NewGuid();

        await svc.AddDependencyAsync(new TwinDependency(
            Guid.NewGuid(), _tenantId, entityA, entityB,
            DependencyType.Feeds, null, 0.9, DateTimeOffset.UtcNow));
        await svc.AddDependencyAsync(new TwinDependency(
            Guid.NewGuid(), _tenantId, entityC, entityA,
            DependencyType.Monitors, null, 0.5, DateTimeOffset.UtcNow));
        await svc.AddDependencyAsync(new TwinDependency(
            Guid.NewGuid(), _tenantId, entityB, entityC,
            DependencyType.Owns, null, 0.3, DateTimeOffset.UtcNow));

        var deps = await svc.GetDependenciesAsync(entityA, _tenantId);

        Assert.Equal(2, deps.Count);
    }

    [Fact]
    public async Task GetDependencies_FiltersByTenant()
    {
        var svc = CreateService();
        var entityA = Guid.NewGuid();

        await svc.AddDependencyAsync(new TwinDependency(
            Guid.NewGuid(), _tenantId, entityA, Guid.NewGuid(),
            DependencyType.DependsOn, null, null, DateTimeOffset.UtcNow));
        await svc.AddDependencyAsync(new TwinDependency(
            Guid.NewGuid(), _otherTenantId, entityA, Guid.NewGuid(),
            DependencyType.DependsOn, null, null, DateTimeOffset.UtcNow));

        var deps = await svc.GetDependenciesAsync(entityA, _tenantId);

        Assert.Single(deps);
    }

    // ── KPIs ───────────────────────────────────────────────────

    [Fact]
    public async Task RecordKpi_StoresAndReturns()
    {
        var svc = CreateService();
        var kpi = new TwinKpi(
            Guid.NewGuid(), "throughput", 95.0, 100.0,
            80.0, 60.0, KpiDirection.HigherIsBetter, "req/s", DateTimeOffset.UtcNow);

        var result = await svc.RecordKpiAsync(kpi);

        Assert.Equal(kpi.MetricName, result.MetricName);
    }

    [Fact]
    public async Task RecordKpi_OverwritesSameEntityAndMetric()
    {
        var svc = CreateService();
        var entityId = Guid.NewGuid();

        await svc.RecordKpiAsync(new TwinKpi(
            entityId, "throughput", 50.0, null, null, null,
            KpiDirection.HigherIsBetter, "req/s", DateTimeOffset.UtcNow));
        await svc.RecordKpiAsync(new TwinKpi(
            entityId, "throughput", 80.0, null, null, null,
            KpiDirection.HigherIsBetter, "req/s", DateTimeOffset.UtcNow));

        var kpis = await svc.GetKpisAsync(entityId);
        Assert.Single(kpis);
        Assert.Equal(80.0, kpis[0].CurrentValue);
    }

    [Fact]
    public async Task GetKpis_FiltersByEntity()
    {
        var svc = CreateService();
        var entityA = Guid.NewGuid();
        var entityB = Guid.NewGuid();

        await svc.RecordKpiAsync(new TwinKpi(
            entityA, "latency", 10.0, null, null, null,
            KpiDirection.LowerIsBetter, "ms", DateTimeOffset.UtcNow));
        await svc.RecordKpiAsync(new TwinKpi(
            entityB, "uptime", 99.9, null, null, null,
            KpiDirection.HigherIsBetter, "%", DateTimeOffset.UtcNow));

        var kpis = await svc.GetKpisAsync(entityA);
        Assert.Single(kpis);
        Assert.Equal("latency", kpis[0].MetricName);
    }

    // ── Bottlenecks ────────────────────────────────────────────

    [Fact]
    public async Task ReportBottleneck_StoresAndReturns()
    {
        var svc = CreateService();
        var bn = new TwinBottleneck(
            Guid.NewGuid(), _tenantId, Guid.NewGuid(),
            "High latency", BottleneckSeverity.High, "DB connection pool",
            false, DateTimeOffset.UtcNow, null);

        var result = await svc.ReportBottleneckAsync(bn);

        Assert.Equal(bn.Id, result.Id);
        Assert.False(result.IsResolved);
    }

    [Fact]
    public async Task ResolveBottleneck_MarksResolved()
    {
        var svc = CreateService();
        var bn = new TwinBottleneck(
            Guid.NewGuid(), _tenantId, Guid.NewGuid(),
            "Slow queue", BottleneckSeverity.Medium, null,
            false, DateTimeOffset.UtcNow, null);
        await svc.ReportBottleneckAsync(bn);

        var resolved = await svc.ResolveBottleneckAsync(bn.Id, _tenantId);

        Assert.NotNull(resolved);
        Assert.True(resolved!.IsResolved);
        Assert.NotNull(resolved.ResolvedAtUtc);
    }

    [Fact]
    public async Task ResolveBottleneck_WrongTenant_ReturnsNull()
    {
        var svc = CreateService();
        var bn = new TwinBottleneck(
            Guid.NewGuid(), _tenantId, Guid.NewGuid(),
            "Issue", BottleneckSeverity.Low, null,
            false, DateTimeOffset.UtcNow, null);
        await svc.ReportBottleneckAsync(bn);

        var result = await svc.ResolveBottleneckAsync(bn.Id, _otherTenantId);

        Assert.Null(result);
    }

    [Fact]
    public async Task ListBottlenecks_ActiveOnly()
    {
        var svc = CreateService();
        await svc.ReportBottleneckAsync(new TwinBottleneck(
            Guid.NewGuid(), _tenantId, Guid.NewGuid(),
            "Active", BottleneckSeverity.High, null,
            false, DateTimeOffset.UtcNow, null));
        await svc.ReportBottleneckAsync(new TwinBottleneck(
            Guid.NewGuid(), _tenantId, Guid.NewGuid(),
            "Resolved", BottleneckSeverity.Low, null,
            true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var active = await svc.ListBottlenecksAsync(_tenantId, activeOnly: true);
        var all = await svc.ListBottlenecksAsync(_tenantId, activeOnly: false);

        Assert.Single(active);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task ListBottlenecks_TenantIsolation()
    {
        var svc = CreateService();
        await svc.ReportBottleneckAsync(new TwinBottleneck(
            Guid.NewGuid(), _tenantId, Guid.NewGuid(),
            "Mine", BottleneckSeverity.Medium, null,
            false, DateTimeOffset.UtcNow, null));
        await svc.ReportBottleneckAsync(new TwinBottleneck(
            Guid.NewGuid(), _otherTenantId, Guid.NewGuid(),
            "Other", BottleneckSeverity.Medium, null,
            false, DateTimeOffset.UtcNow, null));

        var results = await svc.ListBottlenecksAsync(_tenantId);

        Assert.Single(results);
        Assert.Equal("Mine", results[0].Description);
    }

    // ── Artifact Links ─────────────────────────────────────────

    [Fact]
    public async Task LinkArtifact_StoresAndReturns()
    {
        var svc = CreateService();
        var link = new TwinArtifactLink(
            Guid.NewGuid(), Guid.NewGuid(), "Decision", Guid.NewGuid().ToString(),
            "InformedBy", DateTimeOffset.UtcNow);

        var result = await svc.LinkArtifactAsync(link);

        Assert.Equal(link.Id, result.Id);
    }

    [Fact]
    public async Task GetArtifactLinks_FiltersByEntity()
    {
        var svc = CreateService();
        var entityA = Guid.NewGuid();
        var entityB = Guid.NewGuid();

        await svc.LinkArtifactAsync(new TwinArtifactLink(
            Guid.NewGuid(), entityA, "Goal", "g1", "Tracks", DateTimeOffset.UtcNow));
        await svc.LinkArtifactAsync(new TwinArtifactLink(
            Guid.NewGuid(), entityB, "Goal", "g2", "Tracks", DateTimeOffset.UtcNow));

        var links = await svc.GetArtifactLinksAsync(entityA);

        Assert.Single(links);
    }

    // ── Overview ───────────────────────────────────────────────

    [Fact]
    public async Task Overview_AggregatesEntityCounts()
    {
        var svc = CreateService();
        await svc.UpsertEntityAsync(MakeEntity(type: TwinEntityType.Team, name: "T1"));
        await svc.UpsertEntityAsync(MakeEntity(type: TwinEntityType.Team, name: "T2"));
        await svc.UpsertEntityAsync(MakeEntity(type: TwinEntityType.System, name: "S1"));

        var overview = await svc.GetOverviewAsync(_tenantId);

        Assert.Equal(2, overview.EntityCounts["Team"]);
        Assert.Equal(1, overview.EntityCounts["System"]);
    }

    [Fact]
    public async Task Overview_IncludesActiveBottlenecks()
    {
        var svc = CreateService();
        await svc.ReportBottleneckAsync(new TwinBottleneck(
            Guid.NewGuid(), _tenantId, Guid.NewGuid(),
            "Active bn", BottleneckSeverity.Critical, null,
            false, DateTimeOffset.UtcNow, null));
        await svc.ReportBottleneckAsync(new TwinBottleneck(
            Guid.NewGuid(), _tenantId, Guid.NewGuid(),
            "Resolved bn", BottleneckSeverity.Low, null,
            true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var overview = await svc.GetOverviewAsync(_tenantId);

        Assert.Single(overview.ActiveBottlenecks);
        Assert.Equal("Active bn", overview.ActiveBottlenecks[0].Description);
    }

    [Fact]
    public async Task Overview_DetectsWarningKpis_HigherIsBetter()
    {
        var svc = CreateService();
        var entity = MakeEntity();
        await svc.UpsertEntityAsync(entity);

        // Below warning threshold (higher is better, current < warning)
        await svc.RecordKpiAsync(new TwinKpi(
            entity.Id, "throughput", 70.0, 100.0,
            80.0, 50.0, KpiDirection.HigherIsBetter, "req/s", DateTimeOffset.UtcNow));

        var overview = await svc.GetOverviewAsync(_tenantId);

        Assert.Single(overview.WarningKpis);
    }

    [Fact]
    public async Task Overview_DetectsWarningKpis_LowerIsBetter()
    {
        var svc = CreateService();
        var entity = MakeEntity();
        await svc.UpsertEntityAsync(entity);

        // Above warning threshold (lower is better, current > warning)
        await svc.RecordKpiAsync(new TwinKpi(
            entity.Id, "error_rate", 12.0, 5.0,
            10.0, 20.0, KpiDirection.LowerIsBetter, "%", DateTimeOffset.UtcNow));

        var overview = await svc.GetOverviewAsync(_tenantId);

        Assert.Single(overview.WarningKpis);
    }

    [Fact]
    public async Task Overview_HealthyKpi_NotInWarning()
    {
        var svc = CreateService();
        var entity = MakeEntity();
        await svc.UpsertEntityAsync(entity);

        // Above warning threshold (higher is better, current > warning = healthy)
        await svc.RecordKpiAsync(new TwinKpi(
            entity.Id, "throughput", 95.0, 100.0,
            80.0, 50.0, KpiDirection.HigherIsBetter, "req/s", DateTimeOffset.UtcNow));

        var overview = await svc.GetOverviewAsync(_tenantId);

        Assert.Empty(overview.WarningKpis);
    }

    [Fact]
    public async Task Overview_CountsDependencies()
    {
        var svc = CreateService();
        await svc.AddDependencyAsync(new TwinDependency(
            Guid.NewGuid(), _tenantId, Guid.NewGuid(), Guid.NewGuid(),
            DependencyType.DependsOn, null, null, DateTimeOffset.UtcNow));
        await svc.AddDependencyAsync(new TwinDependency(
            Guid.NewGuid(), _otherTenantId, Guid.NewGuid(), Guid.NewGuid(),
            DependencyType.DependsOn, null, null, DateTimeOffset.UtcNow));

        var overview = await svc.GetOverviewAsync(_tenantId);

        Assert.Equal(1, overview.TotalDependencies);
    }

    [Fact]
    public async Task Overview_TenantIsolation()
    {
        var svc = CreateService();
        await svc.UpsertEntityAsync(MakeEntity(name: "Mine"));
        await svc.UpsertEntityAsync(MakeEntity(name: "Other", tenantId: _otherTenantId));

        var overview = await svc.GetOverviewAsync(_tenantId);

        var totalEntities = overview.EntityCounts.Values.Sum();
        Assert.Equal(1, totalEntities);
    }
}

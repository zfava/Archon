using ArchonAI.Api.Security;
using ArchonAI.Memory;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Integration;

public sealed class EnterpriseMemoryTests
{
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<EnterpriseMemoryService> _logger = Substitute.For<ILogger<EnterpriseMemoryService>>();

    private EnterpriseMemoryService CreateService() => new(_eventBus, _logger);

    private readonly Guid _tenantId = Guid.NewGuid();

    private EnterpriseMemoryRecord MakeRecord(
        MemoryLayer layer, string subject = "test",
        Guid? tenantId = null,
        IReadOnlyList<MemoryEntityLink>? links = null,
        IReadOnlyList<string>? tags = null,
        DateTimeOffset? expiresAt = null) =>
        new(
            Id: Guid.NewGuid(),
            TenantId: tenantId ?? _tenantId,
            Layer: layer,
            Category: layer.ToString(),
            Subject: subject,
            Content: $"Content for {subject}",
            Metadata: new Dictionary<string, string>().AsReadOnly(),
            LinkedEntities: links ?? Array.Empty<MemoryEntityLink>(),
            Tags: tags ?? Array.Empty<string>(),
            Importance: 0.7,
            CreatedBy: "test",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: expiresAt);

    // ── Layer Classification ──────────────────────────────────

    [Theory]
    [InlineData(MemoryLayer.Session)]
    [InlineData(MemoryLayer.Operational)]
    [InlineData(MemoryLayer.Organizational)]
    [InlineData(MemoryLayer.Strategic)]
    [InlineData(MemoryLayer.Relational)]
    [InlineData(MemoryLayer.Financial)]
    public async Task Store_PreservesLayer(MemoryLayer layer)
    {
        var svc = CreateService();
        var record = MakeRecord(layer);
        var stored = await svc.StoreAsync(record);

        Assert.Equal(layer, stored.Layer);

        var retrieved = await svc.GetAsync(stored.Id, _tenantId);
        Assert.NotNull(retrieved);
        Assert.Equal(layer, retrieved.Layer);
    }

    [Fact]
    public async Task Store_SessionLayer_SetsDefaultExpiry()
    {
        var svc = CreateService();
        var record = MakeRecord(MemoryLayer.Session);
        Assert.Null(record.ExpiresAtUtc); // no expiry set by caller

        var stored = await svc.StoreAsync(record);
        Assert.NotNull(stored.ExpiresAtUtc);
        Assert.True(stored.ExpiresAtUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Store_NonSessionLayer_NoDefaultExpiry()
    {
        var svc = CreateService();
        var record = MakeRecord(MemoryLayer.Strategic);
        var stored = await svc.StoreAsync(record);
        Assert.Null(stored.ExpiresAtUtc);
    }

    // ── Query by Layer ────────────────────────────────────────

    [Fact]
    public async Task Query_FiltersByLayer()
    {
        var svc = CreateService();
        await svc.StoreAsync(MakeRecord(MemoryLayer.Session, "s1"));
        await svc.StoreAsync(MakeRecord(MemoryLayer.Session, "s2"));
        await svc.StoreAsync(MakeRecord(MemoryLayer.Strategic, "st1"));
        await svc.StoreAsync(MakeRecord(MemoryLayer.Financial, "f1"));

        var sessions = await svc.QueryAsync(_tenantId, MemoryLayer.Session);
        Assert.Equal(2, sessions.TotalCount);
        Assert.All(sessions.Records, r => Assert.Equal(MemoryLayer.Session, r.Layer));

        var strategic = await svc.QueryAsync(_tenantId, MemoryLayer.Strategic);
        Assert.Single(strategic.Records);
    }

    [Fact]
    public async Task Query_ReturnsLayerCounts()
    {
        var svc = CreateService();
        await svc.StoreAsync(MakeRecord(MemoryLayer.Operational, "o1"));
        await svc.StoreAsync(MakeRecord(MemoryLayer.Operational, "o2"));
        await svc.StoreAsync(MakeRecord(MemoryLayer.Relational, "r1"));

        var result = await svc.QueryAsync(_tenantId);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.LayerCounts["Operational"]);
        Assert.Equal(1, result.LayerCounts["Relational"]);
    }

    [Fact]
    public async Task Query_FiltersByCategory()
    {
        var svc = CreateService();
        var r1 = MakeRecord(MemoryLayer.Organizational, "org1") with { Category = "process" };
        var r2 = MakeRecord(MemoryLayer.Organizational, "org2") with { Category = "team" };
        await svc.StoreAsync(r1);
        await svc.StoreAsync(r2);

        var result = await svc.QueryAsync(_tenantId, category: "process");
        Assert.Single(result.Records);
        Assert.Equal("process", result.Records[0].Category);
    }

    [Fact]
    public async Task Query_FiltersByTag()
    {
        var svc = CreateService();
        await svc.StoreAsync(MakeRecord(MemoryLayer.Strategic, "tagged", tags: new[] { "q1", "revenue" }));
        await svc.StoreAsync(MakeRecord(MemoryLayer.Strategic, "untagged", tags: new[] { "q2" }));

        var result = await svc.QueryAsync(_tenantId, tag: "revenue");
        Assert.Single(result.Records);
        Assert.Equal("tagged", result.Records[0].Subject);
    }

    // ── Entity Linkage ────────────────────────────────────────

    [Fact]
    public async Task GetEntityMemory_ReturnsLinkedRecords()
    {
        var svc = CreateService();
        var decisionId = Guid.NewGuid().ToString();

        var links = new[] { new MemoryEntityLink("decision", decisionId, "informed_by") };
        await svc.StoreAsync(MakeRecord(MemoryLayer.Strategic, "strategy context", links: links));
        await svc.StoreAsync(MakeRecord(MemoryLayer.Financial, "budget impact", links: links));
        await svc.StoreAsync(MakeRecord(MemoryLayer.Operational, "unrelated"));

        var view = await svc.GetEntityMemoryAsync(_tenantId, "decision", decisionId);
        Assert.Equal(2, view.Memories.Count);
        Assert.Equal("decision", view.EntityType);
        Assert.Equal(decisionId, view.EntityId);
        Assert.True(view.LayerDistribution.ContainsKey("Strategic"));
        Assert.True(view.LayerDistribution.ContainsKey("Financial"));
    }

    [Fact]
    public async Task GetEntityMemory_EmptyForUnlinkedEntity()
    {
        var svc = CreateService();
        await svc.StoreAsync(MakeRecord(MemoryLayer.Organizational, "test"));

        var view = await svc.GetEntityMemoryAsync(_tenantId, "workflow", "nonexistent");
        Assert.Empty(view.Memories);
    }

    // ── Tenant Isolation ──────────────────────────────────────

    [Fact]
    public async Task Query_TenantIsolation()
    {
        var svc = CreateService();
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        await svc.StoreAsync(MakeRecord(MemoryLayer.Strategic, "t1", tenantId: tenant1));
        await svc.StoreAsync(MakeRecord(MemoryLayer.Strategic, "t2", tenantId: tenant2));

        var t1Result = await svc.QueryAsync(tenant1);
        var t2Result = await svc.QueryAsync(tenant2);

        Assert.Single(t1Result.Records);
        Assert.Single(t2Result.Records);
        Assert.Equal("t1", t1Result.Records[0].Subject);
        Assert.Equal("t2", t2Result.Records[0].Subject);
    }

    [Fact]
    public async Task Get_ReturnsNullForWrongTenant()
    {
        var svc = CreateService();
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        var stored = await svc.StoreAsync(MakeRecord(MemoryLayer.Operational, "t1only", tenantId: tenant1));

        var result = await svc.GetAsync(stored.Id, tenant2);
        Assert.Null(result);
    }

    [Fact]
    public async Task Delete_CannotDeleteOtherTenantRecord()
    {
        var svc = CreateService();
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        var stored = await svc.StoreAsync(MakeRecord(MemoryLayer.Financial, "t1", tenantId: tenant1));
        var deleted = await svc.DeleteAsync(stored.Id, tenant2);
        Assert.False(deleted);

        var still = await svc.GetAsync(stored.Id, tenant1);
        Assert.NotNull(still);
    }

    // ── Retrieval / Persistence ───────────────────────────────

    [Fact]
    public async Task GetAsync_ReturnsStoredRecord()
    {
        var svc = CreateService();
        var record = MakeRecord(MemoryLayer.Relational, "customer intel");
        var stored = await svc.StoreAsync(record);

        var retrieved = await svc.GetAsync(stored.Id, _tenantId);
        Assert.NotNull(retrieved);
        Assert.Equal("customer intel", retrieved.Subject);
        Assert.Equal(MemoryLayer.Relational, retrieved.Layer);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForUnknown()
    {
        var svc = CreateService();
        var result = await svc.GetAsync(Guid.NewGuid(), _tenantId);
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecord()
    {
        var svc = CreateService();
        var stored = await svc.StoreAsync(MakeRecord(MemoryLayer.Session, "to delete"));

        var deleted = await svc.DeleteAsync(stored.Id, _tenantId);
        Assert.True(deleted);

        var result = await svc.GetAsync(stored.Id, _tenantId);
        Assert.Null(result);
    }

    // ── Timeline ──────────────────────────────────────────────

    [Fact]
    public async Task GetTimeline_OrderedByCreatedDesc()
    {
        var svc = CreateService();
        var r1 = MakeRecord(MemoryLayer.Operational, "first") with { CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10) };
        var r2 = MakeRecord(MemoryLayer.Operational, "second") with { CreatedAtUtc = DateTimeOffset.UtcNow };
        await svc.StoreAsync(r1);
        await svc.StoreAsync(r2);

        var timeline = await svc.GetTimelineAsync(_tenantId, MemoryLayer.Operational);
        Assert.Equal(2, timeline.Count);
        Assert.Equal("second", timeline[0].Subject);
        Assert.Equal("first", timeline[1].Subject);
    }

    // ── Session Expiry ────────────────────────────────────────

    [Fact]
    public async Task ExpireSessionMemory_RemovesOldSessions()
    {
        var svc = CreateService();
        var old = MakeRecord(MemoryLayer.Session, "old") with { CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-5) };
        var recent = MakeRecord(MemoryLayer.Session, "recent") with { CreatedAtUtc = DateTimeOffset.UtcNow };
        var strategic = MakeRecord(MemoryLayer.Strategic, "keep") with { CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-5) };
        await svc.StoreAsync(old);
        await svc.StoreAsync(recent);
        await svc.StoreAsync(strategic);

        var expired = await svc.ExpireSessionMemoryAsync(_tenantId, TimeSpan.FromHours(1));
        Assert.Equal(1, expired);

        var all = await svc.QueryAsync(_tenantId);
        Assert.Equal(2, all.TotalCount); // recent session + strategic
    }

    // ── Event Publishing ──────────────────────────────────────

    [Fact]
    public async Task Store_PublishesLayerSpecificEvent()
    {
        var svc = CreateService();
        _eventBus.ClearReceivedCalls();

        await svc.StoreAsync(MakeRecord(MemoryLayer.Financial, "budget memo"));

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "memory.financial.stored"),
            Arg.Any<CancellationToken>());
    }
}

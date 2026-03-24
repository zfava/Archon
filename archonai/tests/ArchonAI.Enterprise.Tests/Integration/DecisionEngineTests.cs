using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Decisions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Integration;

/// <summary>
/// Integration tests for the decision engine: lifecycle management,
/// tenant isolation, artifact linking, and history tracking.
/// </summary>
public sealed class DecisionEngineTests
{
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<DecisionService> _logger = Substitute.For<ILogger<DecisionService>>();

    private DecisionService CreateService() => new(_eventBus, _logger);

    private DecisionRecord MakeDecision(Guid? tenantId = null, string? domain = null,
        DecisionStatus status = DecisionStatus.Draft, string? title = null) =>
        new(
            Id: Guid.NewGuid(),
            TenantId: tenantId ?? Guid.NewGuid(),
            Title: title ?? "Test Decision",
            Domain: domain ?? "operations",
            Objective: "Reduce cost by 15%",
            Constraints: new[] { "Budget < $50k", "Complete in Q2" },
            Assumptions: new[] { "Current vendor pricing stable" },
            Alternatives: new[]
            {
                new DecisionAlternative("opt-a", "Option A", "Lower cost", new[] { "Cheap" }, new[] { "Slow" }, 0.8, 10_000m),
                new DecisionAlternative("opt-b", "Option B", "Faster", new[] { "Fast" }, new[] { "Expensive" }, 0.6, 25_000m),
            },
            RecommendedOptionId: "opt-a",
            Confidence: 0.82,
            Reversibility: DecisionReversibility.PartiallyReversible,
            RiskLevel: DecisionRiskLevel.Medium,
            ExpectedValue: 15_000m,
            RequiresApproval: true,
            LinkedArtifacts: Array.Empty<DecisionLink>(),
            Status: status,
            CreatedBy: "user-1",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

    // ── Create & Retrieve ─────────────────────────────────────────

    [Fact]
    public async Task CreateDecision_StoresAndReturnsDecision()
    {
        var svc = CreateService();
        var decision = MakeDecision();

        var created = await svc.CreateAsync(decision);

        Assert.Equal(decision.Id, created.Id);
        Assert.Equal(decision.Title, created.Title);

        var retrieved = await svc.GetAsync(decision.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(decision.Title, retrieved.Title);
    }

    [Fact]
    public async Task GetDecision_ReturnsNullForUnknownId()
    {
        var svc = CreateService();
        var result = await svc.GetAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateDecision_EmitsEvent()
    {
        var svc = CreateService();
        var decision = MakeDecision();

        await svc.CreateAsync(decision);

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "decision.created"),
            Arg.Any<CancellationToken>());
    }

    // ── Lifecycle ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateStatus_ProgressesThroughLifecycle()
    {
        var svc = CreateService();
        var decision = MakeDecision();
        await svc.CreateAsync(decision);

        var proposed = await svc.UpdateStatusAsync(decision.Id, DecisionStatus.Proposed, "user-1");
        Assert.NotNull(proposed);
        Assert.Equal(DecisionStatus.Proposed, proposed!.Status);

        var approved = await svc.UpdateStatusAsync(decision.Id, DecisionStatus.Approved, "admin-1", "LGTM");
        Assert.NotNull(approved);
        Assert.Equal(DecisionStatus.Approved, approved!.Status);

        var executing = await svc.UpdateStatusAsync(decision.Id, DecisionStatus.Executing, "system");
        Assert.Equal(DecisionStatus.Executing, executing!.Status);

        var completed = await svc.UpdateStatusAsync(decision.Id, DecisionStatus.Completed, "system");
        Assert.Equal(DecisionStatus.Completed, completed!.Status);
    }

    [Fact]
    public async Task UpdateStatus_ReturnsNullForUnknownId()
    {
        var svc = CreateService();
        var result = await svc.UpdateStatusAsync(Guid.NewGuid(), DecisionStatus.Approved, "user-1");
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateStatus_RecordsHistoryEvents()
    {
        var svc = CreateService();
        var decision = MakeDecision();
        await svc.CreateAsync(decision);

        await svc.UpdateStatusAsync(decision.Id, DecisionStatus.Proposed, "user-1");
        await svc.UpdateStatusAsync(decision.Id, DecisionStatus.Approved, "admin-1");

        var history = await svc.GetHistoryAsync(decision.Id);

        // Created + 2 status changes = 3 events
        Assert.Equal(3, history.Count);
        Assert.Equal("decision.created", history[0].EventType);
        Assert.Equal("decision.status.proposed", history[1].EventType);
        Assert.Equal("decision.status.approved", history[2].EventType);
    }

    // ── Tenant Isolation ──────────────────────────────────────────

    [Fact]
    public async Task ListDecisions_FiltersByTenantId()
    {
        var svc = CreateService();
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        await svc.CreateAsync(MakeDecision(tenantId: tenant1, title: "T1-A"));
        await svc.CreateAsync(MakeDecision(tenantId: tenant1, title: "T1-B"));
        await svc.CreateAsync(MakeDecision(tenantId: tenant2, title: "T2-A"));

        var t1Results = await svc.ListAsync(tenant1);
        var t2Results = await svc.ListAsync(tenant2);

        Assert.Equal(2, t1Results.Count);
        Assert.All(t1Results, d => Assert.Equal(tenant1, d.TenantId));

        Assert.Single(t2Results);
        Assert.Equal(tenant2, t2Results[0].TenantId);
    }

    [Fact]
    public async Task ListDecisions_TenantCannotSeeCrossTenantDecisions()
    {
        var svc = CreateService();
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        await svc.CreateAsync(MakeDecision(tenantId: tenant1, title: "Secret Decision"));

        var t2Results = await svc.ListAsync(tenant2);
        Assert.Empty(t2Results);
    }

    // ── Filtering ─────────────────────────────────────────────────

    [Fact]
    public async Task ListDecisions_FiltersByDomain()
    {
        var svc = CreateService();
        var tenant = Guid.NewGuid();

        await svc.CreateAsync(MakeDecision(tenantId: tenant, domain: "finance"));
        await svc.CreateAsync(MakeDecision(tenantId: tenant, domain: "operations"));
        await svc.CreateAsync(MakeDecision(tenantId: tenant, domain: "finance"));

        var financeOnly = await svc.ListAsync(tenant, domain: "finance");
        Assert.Equal(2, financeOnly.Count);
        Assert.All(financeOnly, d => Assert.Equal("finance", d.Domain));
    }

    [Fact]
    public async Task ListDecisions_FiltersByStatus()
    {
        var svc = CreateService();
        var tenant = Guid.NewGuid();

        var d1 = MakeDecision(tenantId: tenant);
        var d2 = MakeDecision(tenantId: tenant);
        await svc.CreateAsync(d1);
        await svc.CreateAsync(d2);
        await svc.UpdateStatusAsync(d1.Id, DecisionStatus.Approved, "admin");

        var approvedOnly = await svc.ListAsync(tenant, status: DecisionStatus.Approved);
        Assert.Single(approvedOnly);
        Assert.Equal(d1.Id, approvedOnly[0].Id);
    }

    // ── Artifact Linking ──────────────────────────────────────────

    [Fact]
    public async Task LinkArtifact_AddsLinkToDecision()
    {
        var svc = CreateService();
        var decision = MakeDecision();
        await svc.CreateAsync(decision);

        var link = new DecisionLink("workflow", "wf-123", "Execute cost reduction workflow", DateTimeOffset.UtcNow);
        var updated = await svc.LinkArtifactAsync(decision.Id, link);

        Assert.NotNull(updated);
        Assert.Single(updated!.LinkedArtifacts);
        Assert.Equal("workflow", updated.LinkedArtifacts[0].ArtifactType);
        Assert.Equal("wf-123", updated.LinkedArtifacts[0].ArtifactId);
    }

    [Fact]
    public async Task LinkArtifact_SupportsMultipleLinks()
    {
        var svc = CreateService();
        var decision = MakeDecision();
        await svc.CreateAsync(decision);

        await svc.LinkArtifactAsync(decision.Id,
            new DecisionLink("workflow", "wf-1", "Primary workflow", DateTimeOffset.UtcNow));
        var updated = await svc.LinkArtifactAsync(decision.Id,
            new DecisionLink("approval", "appr-1", "Budget approval gate", DateTimeOffset.UtcNow));

        Assert.NotNull(updated);
        Assert.Equal(2, updated!.LinkedArtifacts.Count);
    }

    [Fact]
    public async Task LinkArtifact_ReturnsNullForUnknownDecision()
    {
        var svc = CreateService();
        var result = await svc.LinkArtifactAsync(Guid.NewGuid(),
            new DecisionLink("workflow", "wf-1", "test", DateTimeOffset.UtcNow));
        Assert.Null(result);
    }

    // ── Decision Data Integrity ───────────────────────────────────

    [Fact]
    public async Task DecisionRecord_PreservesAlternativesAndRationale()
    {
        var svc = CreateService();
        var decision = MakeDecision();
        await svc.CreateAsync(decision);

        var retrieved = await svc.GetAsync(decision.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(2, retrieved!.Alternatives.Count);
        Assert.Equal("Option A", retrieved.Alternatives[0].Title);
        Assert.Equal("Option B", retrieved.Alternatives[1].Title);
        Assert.Equal("opt-a", retrieved.RecommendedOptionId);
        Assert.Equal(0.82, retrieved.Confidence);
        Assert.Equal(DecisionReversibility.PartiallyReversible, retrieved.Reversibility);
        Assert.Equal(DecisionRiskLevel.Medium, retrieved.RiskLevel);
        Assert.Equal(15_000m, retrieved.ExpectedValue);
    }

    [Fact]
    public async Task DecisionRecord_PreservesConstraintsAndAssumptions()
    {
        var svc = CreateService();
        var decision = MakeDecision();
        await svc.CreateAsync(decision);

        var retrieved = await svc.GetAsync(decision.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(2, retrieved!.Constraints.Count);
        Assert.Contains("Budget < $50k", retrieved.Constraints);
        Assert.Single(retrieved.Assumptions);
        Assert.Contains("Current vendor pricing stable", retrieved.Assumptions);
    }

    // ── History ───────────────────────────────────────────────────

    [Fact]
    public async Task GetHistory_ReturnsEmptyForUnknownDecision()
    {
        var svc = CreateService();
        var history = await svc.GetHistoryAsync(Guid.NewGuid());
        Assert.Empty(history);
    }

    [Fact]
    public async Task LinkArtifact_RecordsHistoryEvent()
    {
        var svc = CreateService();
        var decision = MakeDecision();
        await svc.CreateAsync(decision);

        await svc.LinkArtifactAsync(decision.Id,
            new DecisionLink("workflow", "wf-1", "Test link", DateTimeOffset.UtcNow));

        var history = await svc.GetHistoryAsync(decision.Id);
        Assert.Equal(2, history.Count); // created + linked
        Assert.Equal("decision.artifact.linked", history[1].EventType);
    }
}

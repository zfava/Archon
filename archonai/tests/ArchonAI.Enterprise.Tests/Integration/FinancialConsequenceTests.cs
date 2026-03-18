using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Decisions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Integration;

public sealed class FinancialConsequenceTests
{
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<FinancialConsequenceService> _logger = Substitute.For<ILogger<FinancialConsequenceService>>();

    private FinancialConsequenceService CreateService() => new(_eventBus, _logger);

    private static FinancialConsequence MakeConsequence(Guid? decisionId = null, Guid? tenantId = null) =>
        new(
            Id: Guid.NewGuid(),
            DecisionId: decisionId ?? Guid.NewGuid(),
            TenantId: tenantId ?? Guid.NewGuid(),
            ExpectedRevenueImpactLow: 50_000m,
            ExpectedRevenueImpactHigh: 120_000m,
            ExpectedCostImpactLow: -15_000m,
            ExpectedCostImpactHigh: -8_000m,
            ExpectedMarginImpact: 35_000m,
            ExpectedCashTimingImpact: "Revenue delayed 30 days post-implementation",
            LaborImpact: "2 FTE reallocation for 6 weeks",
            DownsideRisk: -40_000m,
            UpsidePotential: 200_000m,
            ConfidenceAdjustment: 0.75,
            RoiEstimateLow: 120_000m,
            RoiEstimateHigh: 350_000m,
            BreakEvenEstimate: "4-6 months",
            Assumptions: new[] { "Vendor pricing stable", "Market conditions unchanged" },
            Notes: "Conservative estimate based on Q1 data",
            CreatedBy: "analyst-1",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

    // ── Attach & Retrieve ─────────────────────────────────────

    [Fact]
    public async Task Attach_StoresAndReturnsConsequence()
    {
        var svc = CreateService();
        var fc = MakeConsequence();

        var created = await svc.AttachAsync(fc);

        Assert.Equal(fc.Id, created.Id);
        Assert.Equal(fc.DecisionId, created.DecisionId);
        Assert.Equal(50_000m, created.ExpectedRevenueImpactLow);
        Assert.Equal(120_000m, created.ExpectedRevenueImpactHigh);
    }

    [Fact]
    public async Task Attach_EmitsEvent()
    {
        var svc = CreateService();
        var fc = MakeConsequence();

        await svc.AttachAsync(fc);

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "decision.financial_consequence.attached"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByDecision_ReturnsAttachedConsequence()
    {
        var svc = CreateService();
        var decisionId = Guid.NewGuid();
        var fc = MakeConsequence(decisionId: decisionId);
        await svc.AttachAsync(fc);

        var retrieved = await svc.GetByDecisionAsync(decisionId);

        Assert.NotNull(retrieved);
        Assert.Equal(decisionId, retrieved!.DecisionId);
        Assert.Equal(fc.ExpectedMarginImpact, retrieved.ExpectedMarginImpact);
        Assert.Equal(fc.DownsideRisk, retrieved.DownsideRisk);
        Assert.Equal(fc.UpsidePotential, retrieved.UpsidePotential);
    }

    [Fact]
    public async Task GetByDecision_ReturnsNullForUnknown()
    {
        var svc = CreateService();
        var result = await svc.GetByDecisionAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    // ── Update ────────────────────────────────────────────────

    [Fact]
    public async Task Update_ModifiesExistingConsequence()
    {
        var svc = CreateService();
        var decisionId = Guid.NewGuid();
        var fc = MakeConsequence(decisionId: decisionId);
        await svc.AttachAsync(fc);

        var updated = fc with
        {
            ExpectedRevenueImpactHigh = 250_000m,
            RoiEstimateHigh = 500_000m,
            Notes = "Revised after Q2 forecast",
        };

        var result = await svc.UpdateAsync(decisionId, updated);

        Assert.NotNull(result);
        Assert.Equal(250_000m, result!.ExpectedRevenueImpactHigh);
        Assert.Equal(500_000m, result.RoiEstimateHigh);
        Assert.Equal("Revised after Q2 forecast", result.Notes);
    }

    [Fact]
    public async Task Update_ReturnsNullForUnknownDecision()
    {
        var svc = CreateService();
        var fc = MakeConsequence();

        var result = await svc.UpdateAsync(Guid.NewGuid(), fc);
        Assert.Null(result);
    }

    [Fact]
    public async Task Update_EmitsEvent()
    {
        var svc = CreateService();
        var decisionId = Guid.NewGuid();
        var fc = MakeConsequence(decisionId: decisionId);
        await svc.AttachAsync(fc);
        _eventBus.ClearReceivedCalls();

        await svc.UpdateAsync(decisionId, fc with { ExpectedMarginImpact = 99_000m });

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "decision.financial_consequence.updated"),
            Arg.Any<CancellationToken>());
    }

    // ── Partial / Null Data ───────────────────────────────────

    [Fact]
    public async Task Attach_HandlesPartialDataSafely()
    {
        var svc = CreateService();
        var sparse = new FinancialConsequence(
            Id: Guid.NewGuid(),
            DecisionId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            ExpectedRevenueImpactLow: null,
            ExpectedRevenueImpactHigh: null,
            ExpectedCostImpactLow: null,
            ExpectedCostImpactHigh: null,
            ExpectedMarginImpact: null,
            ExpectedCashTimingImpact: null,
            LaborImpact: null,
            DownsideRisk: null,
            UpsidePotential: null,
            ConfidenceAdjustment: null,
            RoiEstimateLow: null,
            RoiEstimateHigh: null,
            BreakEvenEstimate: null,
            Assumptions: Array.Empty<string>(),
            Notes: null,
            CreatedBy: "user-1",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        var created = await svc.AttachAsync(sparse);

        Assert.NotNull(created);
        Assert.Null(created.ExpectedRevenueImpactLow);
        Assert.Null(created.DownsideRisk);
        Assert.Empty(created.Assumptions);

        var retrieved = await svc.GetByDecisionAsync(sparse.DecisionId);
        Assert.NotNull(retrieved);
    }

    [Fact]
    public async Task Attach_PreservesAssumptionsAndNotes()
    {
        var svc = CreateService();
        var fc = MakeConsequence();
        await svc.AttachAsync(fc);

        var retrieved = await svc.GetByDecisionAsync(fc.DecisionId);

        Assert.NotNull(retrieved);
        Assert.Equal(2, retrieved!.Assumptions.Count);
        Assert.Contains("Vendor pricing stable", retrieved.Assumptions);
        Assert.Contains("Market conditions unchanged", retrieved.Assumptions);
        Assert.Equal("Conservative estimate based on Q1 data", retrieved.Notes);
    }

    // ── Tenant Isolation ──────────────────────────────────────

    [Fact]
    public async Task TenantIsolation_ConsequenceScopedByDecision()
    {
        var svc = CreateService();
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();
        var dec1 = Guid.NewGuid();
        var dec2 = Guid.NewGuid();

        await svc.AttachAsync(MakeConsequence(decisionId: dec1, tenantId: tenant1));
        await svc.AttachAsync(MakeConsequence(decisionId: dec2, tenantId: tenant2));

        var fc1 = await svc.GetByDecisionAsync(dec1);
        var fc2 = await svc.GetByDecisionAsync(dec2);

        Assert.NotNull(fc1);
        Assert.NotNull(fc2);
        Assert.Equal(tenant1, fc1!.TenantId);
        Assert.Equal(tenant2, fc2!.TenantId);
        Assert.NotEqual(fc1.TenantId, fc2.TenantId);
    }

    // ── Persistence Linkage ───────────────────────────────────

    [Fact]
    public async Task Attach_OverwritesPreviousConsequence()
    {
        var svc = CreateService();
        var decisionId = Guid.NewGuid();

        await svc.AttachAsync(MakeConsequence(decisionId: decisionId) with { DownsideRisk = -10_000m });
        await svc.AttachAsync(MakeConsequence(decisionId: decisionId) with { DownsideRisk = -50_000m });

        var latest = await svc.GetByDecisionAsync(decisionId);
        Assert.NotNull(latest);
        Assert.Equal(-50_000m, latest!.DownsideRisk);
    }
}

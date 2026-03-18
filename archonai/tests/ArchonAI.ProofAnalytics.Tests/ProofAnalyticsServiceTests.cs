using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Decisions;
using ArchonAI.Core.Models.ProofAnalytics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArchonAI.ProofAnalytics.Tests;

public sealed class ProofAnalyticsServiceTests
{
    private readonly IDecisionService _decisions = Substitute.For<IDecisionService>();
    private readonly IOutcomeLearningService _outcomes = Substitute.For<IOutcomeLearningService>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<ProofAnalyticsService> _logger = Substitute.For<ILogger<ProofAnalyticsService>>();

    private ProofAnalyticsService CreateService() =>
        new(_decisions, _outcomes, _eventBus, _logger);

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private static ProofEvent MakeEvent(
        Guid tenantId, Guid decisionId, ProofEventType eventType,
        string? actionType = null, bool? isSuccess = null,
        string? overrideReason = null, Guid? workflowId = null,
        decimal? expectedValue = null, decimal? actualValue = null) =>
        new(
            Id: Guid.NewGuid(), TenantId: tenantId, DecisionId: decisionId,
            WorkflowId: workflowId, EventType: eventType, Actor: "test-user",
            Detail: "test detail", ExpectedValue: expectedValue, ActualValue: actualValue,
            Variance: null, VariancePercent: null, ActionType: actionType,
            IsSuccess: isSuccess, OverrideReason: overrideReason,
            EconomicImpact: null, ImpactAttribution: null,
            OccurredAtUtc: DateTimeOffset.UtcNow);

    private static DecisionRecord MakeDecision(Guid tenantId, Guid decisionId, string domain = "ops") =>
        new(
            Id: decisionId, TenantId: tenantId, Title: "Test Decision",
            Domain: domain, Objective: "test", Constraints: [], Assumptions: [],
            Alternatives: [], RecommendedOptionId: "", Confidence: 0.8,
            Reversibility: DecisionReversibility.FullyReversible,
            RiskLevel: DecisionRiskLevel.Medium, ExpectedValue: 1000m,
            RequiresApproval: true, LinkedArtifacts: [],
            Status: DecisionStatus.Completed, CreatedBy: "test",
            CreatedAtUtc: DateTimeOffset.UtcNow.AddHours(-2),
            UpdatedAtUtc: DateTimeOffset.UtcNow);

    // ── Tenant Isolation ──────────────────────────────────────

    [Fact]
    public async Task TenantIsolation_EventsFromDifferentTenants_AreNotMixed()
    {
        var svc = CreateService();
        var dA = Guid.NewGuid();
        var dB = Guid.NewGuid();

        await svc.RecordEventAsync(MakeEvent(TenantA, dA, ProofEventType.DecisionCreated, actionType: "test"));
        await svc.RecordEventAsync(MakeEvent(TenantA, dA, ProofEventType.ActionExecuted, actionType: "test", isSuccess: true));
        await svc.RecordEventAsync(MakeEvent(TenantB, dB, ProofEventType.DecisionCreated, actionType: "test"));

        var trendsA = await svc.GetExecutionTrendsAsync(TenantA);
        var trendsB = await svc.GetExecutionTrendsAsync(TenantB);

        trendsA.TotalExecutions.Should().Be(1);
        trendsB.TotalExecutions.Should().Be(0);
    }

    [Fact]
    public async Task TenantIsolation_OverrideRates_AreIsolated()
    {
        var svc = CreateService();
        var dA = Guid.NewGuid();
        var dB = Guid.NewGuid();

        await svc.RecordEventAsync(MakeEvent(TenantA, dA, ProofEventType.OverrideApplied, overrideReason: "policy"));
        await svc.RecordEventAsync(MakeEvent(TenantB, dB, ProofEventType.DecisionCreated));

        var ratesA = await svc.GetOverrideRatesAsync(TenantA);
        var ratesB = await svc.GetOverrideRatesAsync(TenantB);

        ratesA.Overrides.Should().Be(1);
        ratesB.Overrides.Should().Be(0);
    }

    // ── Lineage Integrity ─────────────────────────────────────

    [Fact]
    public async Task GetTimeline_ReturnsEventsInChronologicalOrder()
    {
        var svc = CreateService();
        var dId = Guid.NewGuid();
        var decision = MakeDecision(TenantA, dId);

        _decisions.GetAsync(dId, Arg.Any<CancellationToken>()).Returns(decision);
        _outcomes.GetOutcomeAsync(dId, Arg.Any<CancellationToken>()).Returns((OutcomeRecord?)null);

        await svc.RecordEventAsync(MakeEvent(TenantA, dId, ProofEventType.DecisionCreated));
        await svc.RecordEventAsync(MakeEvent(TenantA, dId, ProofEventType.RecommendationMade));
        await svc.RecordEventAsync(MakeEvent(TenantA, dId, ProofEventType.ApprovalGranted));
        await svc.RecordEventAsync(MakeEvent(TenantA, dId, ProofEventType.ActionExecuted, isSuccess: true));

        var timeline = await svc.GetTimelineAsync(dId);

        timeline.Should().NotBeNull();
        timeline!.Events.Should().HaveCount(4);
        timeline.DecisionId.Should().Be(dId);
        timeline.DecisionTitle.Should().Be("Test Decision");
        // Events should be in chronological order
        for (int i = 1; i < timeline.Events.Count; i++)
        {
            timeline.Events[i].OccurredAtUtc.Should().BeOnOrAfter(timeline.Events[i - 1].OccurredAtUtc);
        }
    }

    [Fact]
    public async Task GetTimeline_WithOutcome_ShowsVarianceInSummary()
    {
        var svc = CreateService();
        var dId = Guid.NewGuid();
        var decision = MakeDecision(TenantA, dId);

        var outcome = new OutcomeRecord(
            Guid.NewGuid(), dId, TenantA,
            "Expected growth", 1000m, 0.8, "Q1",
            "Actual growth", 1200m, DateTimeOffset.UtcNow,
            200m, 20.0, OutcomeDirection.Overperformed,
            null, null, OutcomeAssessment.BetterThanExpected,
            RecalibrationSignal.ConfidenceDeflated,
            "test", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        _decisions.GetAsync(dId, Arg.Any<CancellationToken>()).Returns(decision);
        _outcomes.GetOutcomeAsync(dId, Arg.Any<CancellationToken>()).Returns(outcome);

        await svc.RecordEventAsync(MakeEvent(TenantA, dId, ProofEventType.DecisionCreated));

        var timeline = await svc.GetTimelineAsync(dId);

        timeline.Should().NotBeNull();
        timeline!.Summary.HasOutcome.Should().BeTrue();
        timeline.Summary.PredictedValue.Should().Be(1000m);
        timeline.Summary.ActualValue.Should().Be(1200m);
        timeline.Summary.Variance.Should().Be(200m);
        timeline.Summary.FinalAssessment.Should().Be("BetterThanExpected");
    }

    [Fact]
    public async Task GetTimeline_WithOverride_MarksOverrideInSummary()
    {
        var svc = CreateService();
        var dId = Guid.NewGuid();
        var decision = MakeDecision(TenantA, dId);

        _decisions.GetAsync(dId, Arg.Any<CancellationToken>()).Returns(decision);
        _outcomes.GetOutcomeAsync(dId, Arg.Any<CancellationToken>()).Returns((OutcomeRecord?)null);

        await svc.RecordEventAsync(MakeEvent(TenantA, dId, ProofEventType.OverrideApplied, overrideReason: "compliance"));

        var timeline = await svc.GetTimelineAsync(dId);

        timeline!.Summary.WasOverridden.Should().BeTrue();
        timeline.Summary.WasReversed.Should().BeFalse();
    }

    // ── Aggregation Correctness ───────────────────────────────

    [Fact]
    public async Task ApprovalConversion_CorrectlyAggregates()
    {
        var svc = CreateService();
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();

        await svc.RecordEventAsync(MakeEvent(TenantA, d1, ProofEventType.ApprovalRequested, actionType: "deploy"));
        await svc.RecordEventAsync(MakeEvent(TenantA, d1, ProofEventType.ApprovalGranted, actionType: "deploy"));
        await svc.RecordEventAsync(MakeEvent(TenantA, d1, ProofEventType.ActionExecuted, actionType: "deploy", isSuccess: true));
        await svc.RecordEventAsync(MakeEvent(TenantA, d2, ProofEventType.ApprovalRequested, actionType: "deploy"));
        await svc.RecordEventAsync(MakeEvent(TenantA, d2, ProofEventType.ApprovalDenied, actionType: "deploy"));

        var summary = await svc.GetApprovalConversionAsync(TenantA);

        summary.TotalApprovalRequests.Should().Be(2);
        summary.Granted.Should().Be(1);
        summary.Denied.Should().Be(1);
        summary.ExecutedAfterApproval.Should().Be(1);
        summary.ApprovalRate.Should().Be(0.5);
        summary.ExecutionConversionRate.Should().Be(1.0);
    }

    [Fact]
    public async Task ExecutionTrends_CountsSuccessesAndFailures()
    {
        var svc = CreateService();
        var dId = Guid.NewGuid();

        await svc.RecordEventAsync(MakeEvent(TenantA, dId, ProofEventType.ActionExecuted, isSuccess: true));
        await svc.RecordEventAsync(MakeEvent(TenantA, dId, ProofEventType.ActionExecuted, isSuccess: true));
        await svc.RecordEventAsync(MakeEvent(TenantA, dId, ProofEventType.ActionExecuted, isSuccess: false));

        var trends = await svc.GetExecutionTrendsAsync(TenantA);

        trends.TotalExecutions.Should().Be(3);
        trends.Successes.Should().Be(2);
        trends.Failures.Should().Be(1);
        trends.SuccessRate.Should().BeApproximately(2.0 / 3.0, 0.01);
    }

    [Fact]
    public async Task OverrideRates_CalculatesCorrectly()
    {
        var svc = CreateService();
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();
        var d3 = Guid.NewGuid();

        await svc.RecordEventAsync(MakeEvent(TenantA, d1, ProofEventType.DecisionCreated));
        await svc.RecordEventAsync(MakeEvent(TenantA, d2, ProofEventType.DecisionCreated));
        await svc.RecordEventAsync(MakeEvent(TenantA, d3, ProofEventType.DecisionCreated));
        await svc.RecordEventAsync(MakeEvent(TenantA, d1, ProofEventType.OverrideApplied, overrideReason: "compliance"));
        await svc.RecordEventAsync(MakeEvent(TenantA, d2, ProofEventType.ReversalApplied));

        var rates = await svc.GetOverrideRatesAsync(TenantA);

        rates.TotalDecisions.Should().Be(3);
        rates.Overrides.Should().Be(1);
        rates.Reversals.Should().Be(1);
        rates.OverrideRate.Should().BeApproximately(1.0 / 3.0, 0.01);
        rates.ReversalRate.Should().BeApproximately(1.0 / 3.0, 0.01);
        rates.OverrideReasonDistribution.Should().ContainKey("compliance").WhoseValue.Should().Be(1);
    }

    // ── API Correctness (service methods returning expected shapes) ──

    [Fact]
    public async Task GetTimeline_NonExistentDecision_ReturnsNull()
    {
        var svc = CreateService();
        _decisions.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((DecisionRecord?)null);

        var timeline = await svc.GetTimelineAsync(Guid.NewGuid());
        timeline.Should().BeNull();
    }

    [Fact]
    public async Task PredictedVsActual_EmptyTenant_ReturnsZeros()
    {
        var svc = CreateService();
        _decisions.ListAsync(TenantA, null, null, 50, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DecisionRecord>() as IReadOnlyList<DecisionRecord>);

        var pva = await svc.GetPredictedVsActualAsync(TenantA);

        pva.TotalDecisions.Should().Be(0);
        pva.WithOutcomes.Should().Be(0);
        pva.AccuracyRate.Should().Be(0);
    }

    [Fact]
    public async Task WorkflowTimelines_LinkDecisionsToWorkflow()
    {
        var svc = CreateService();
        var wfId = Guid.NewGuid();
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();

        var dec1 = MakeDecision(TenantA, d1);
        var dec2 = MakeDecision(TenantA, d2);
        _decisions.GetAsync(d1, Arg.Any<CancellationToken>()).Returns(dec1);
        _decisions.GetAsync(d2, Arg.Any<CancellationToken>()).Returns(dec2);
        _outcomes.GetOutcomeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((OutcomeRecord?)null);

        await svc.RecordEventAsync(MakeEvent(TenantA, d1, ProofEventType.DecisionCreated, workflowId: wfId));
        await svc.RecordEventAsync(MakeEvent(TenantA, d2, ProofEventType.DecisionCreated, workflowId: wfId));

        var timelines = await svc.GetWorkflowTimelinesAsync(wfId);

        timelines.Should().HaveCount(2);
    }

    [Fact]
    public async Task Dashboard_ReturnsAllSections()
    {
        var svc = CreateService();
        _decisions.ListAsync(TenantA, null, null, 100, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DecisionRecord>() as IReadOnlyList<DecisionRecord>);

        var dashboard = await svc.GetDashboardAsync(TenantA);

        dashboard.Should().NotBeNull();
        dashboard.TenantId.Should().Be(TenantA);
        dashboard.PredictedVsActual.Should().NotBeNull();
        dashboard.ApprovalConversion.Should().NotBeNull();
        dashboard.ExecutionTrends.Should().NotBeNull();
        dashboard.OverrideRates.Should().NotBeNull();
        dashboard.TrustAnalytics.Should().NotBeNull();
    }

    [Fact]
    public async Task RecordEvent_PublishesSystemEvent()
    {
        var svc = CreateService();
        var evt = MakeEvent(TenantA, Guid.NewGuid(), ProofEventType.DecisionCreated);

        await svc.RecordEventAsync(evt);

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(se => se.EventType == "proof.event.recorded"),
            Arg.Any<CancellationToken>());
    }
}

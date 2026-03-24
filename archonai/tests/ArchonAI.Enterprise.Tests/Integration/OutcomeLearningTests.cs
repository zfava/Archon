using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Decisions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Integration;

public sealed class OutcomeLearningTests
{
    private readonly IDecisionService _decisionService = Substitute.For<IDecisionService>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<OutcomeLearningService> _logger = Substitute.For<ILogger<OutcomeLearningService>>();

    private OutcomeLearningService CreateService() => new(_decisionService, _eventBus, _logger);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _decisionId = Guid.NewGuid();

    // ── Expected Outcome Recording ────────────────────────────

    [Fact]
    public async Task RecordExpected_CreatesRecord()
    {
        var svc = CreateService();
        var record = await svc.RecordExpectedOutcomeAsync(
            _decisionId, _tenantId, "Revenue boost", 50_000m, 0.85, "Q2 2026", "admin");

        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.Equal(_decisionId, record.DecisionId);
        Assert.Equal(_tenantId, record.TenantId);
        Assert.Equal("Revenue boost", record.ExpectedOutcomeSummary);
        Assert.Equal(50_000m, record.ExpectedValue);
        Assert.Equal(0.85, record.ConfidenceAtPrediction);
        Assert.Equal(OutcomeDirection.Pending, record.Direction);
        Assert.Equal(OutcomeAssessment.Pending, record.Assessment);
    }

    [Fact]
    public async Task RecordExpected_PublishesEvent()
    {
        var svc = CreateService();
        await svc.RecordExpectedOutcomeAsync(
            _decisionId, _tenantId, null, 10_000m, 0.7, null, "admin");

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "outcome.expected.recorded"),
            Arg.Any<CancellationToken>());
    }

    // ── Actual Outcome Recording ──────────────────────────────

    [Fact]
    public async Task RecordActual_ComputesVariance()
    {
        var svc = CreateService();
        await svc.RecordExpectedOutcomeAsync(
            _decisionId, _tenantId, "Revenue boost", 50_000m, 0.80, null, "admin");

        var record = await svc.RecordActualOutcomeAsync(
            _decisionId, "Revenue exceeded", 65_000m, null, null, "analyst");

        Assert.Equal(65_000m, record.ActualValue);
        Assert.Equal(15_000m, record.ValueVariance);
        Assert.True(Math.Abs(record.VariancePercent!.Value - 30.0) < 0.5);
        Assert.Equal(OutcomeDirection.Overperformed, record.Direction);
        Assert.Equal(OutcomeAssessment.BetterThanExpected, record.Assessment);
    }

    [Fact]
    public async Task RecordActual_OnTarget_WhenWithin10Percent()
    {
        var svc = CreateService();
        await svc.RecordExpectedOutcomeAsync(
            _decisionId, _tenantId, "Savings", 100_000m, 0.9, null, "admin");

        var record = await svc.RecordActualOutcomeAsync(
            _decisionId, "Savings realized", 105_000m, null, null, "analyst");

        Assert.Equal(OutcomeDirection.OnTarget, record.Direction);
        Assert.Equal(OutcomeAssessment.AsExpected, record.Assessment);
        Assert.Equal(RecalibrationSignal.ConfidenceCalibrated, record.RecalibrationSignal);
    }

    [Fact]
    public async Task RecordActual_Underperformed()
    {
        var svc = CreateService();
        await svc.RecordExpectedOutcomeAsync(
            _decisionId, _tenantId, "Growth", 100_000m, 0.80, null, "admin");

        var record = await svc.RecordActualOutcomeAsync(
            _decisionId, "Fell short", 70_000m, "Market downturn", null, "analyst");

        Assert.Equal(OutcomeDirection.Underperformed, record.Direction);
        Assert.Equal(OutcomeAssessment.WorseThanExpected, record.Assessment);
        Assert.Equal("Market downturn", record.RootCause);
    }

    [Fact]
    public async Task RecordActual_FailsWithoutExpected()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.RecordActualOutcomeAsync(Guid.NewGuid(), "result", 100m, null, null, "analyst"));
    }

    [Fact]
    public async Task RecordActual_PublishesEvent()
    {
        var svc = CreateService();
        _eventBus.ClearReceivedCalls();
        await svc.RecordExpectedOutcomeAsync(
            _decisionId, _tenantId, null, 1000m, 0.5, null, "admin");
        _eventBus.ClearReceivedCalls();

        await svc.RecordActualOutcomeAsync(
            _decisionId, null, 500m, null, null, "analyst");

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "outcome.actual.recorded"),
            Arg.Any<CancellationToken>());
    }

    // ── Variance Computation ──────────────────────────────────

    [Fact]
    public void ComputeVariance_CorrectValues()
    {
        var (variance, pct) = OutcomeLearningService.ComputeVariance(100m, 130m);
        Assert.Equal(30m, variance);
        Assert.Equal(30.0, pct!.Value, 1);
    }

    [Fact]
    public void ComputeVariance_NullWhenMissing()
    {
        var (v1, p1) = OutcomeLearningService.ComputeVariance(null, 100m);
        Assert.Null(v1);
        Assert.Null(p1);

        var (v2, p2) = OutcomeLearningService.ComputeVariance(100m, null);
        Assert.Null(v2);
        Assert.Null(p2);
    }

    [Fact]
    public void ComputeVariance_NegativeVariance()
    {
        var (variance, pct) = OutcomeLearningService.ComputeVariance(100m, 60m);
        Assert.Equal(-40m, variance);
        Assert.Equal(-40.0, pct!.Value, 1);
    }

    // ── Recalibration Signal ──────────────────────────────────

    [Fact]
    public void Signal_ConfidenceInflated_HighConfBadOutcome()
    {
        var signal = OutcomeLearningService.DetermineRecalibrationSignal(
            0.90, OutcomeDirection.Underperformed, -30.0);
        Assert.Equal(RecalibrationSignal.ConfidenceInflated, signal);
    }

    [Fact]
    public void Signal_AssumptionInvalid_HighConfMassiveMiss()
    {
        var signal = OutcomeLearningService.DetermineRecalibrationSignal(
            0.85, OutcomeDirection.Underperformed, -60.0);
        Assert.Equal(RecalibrationSignal.AssumptionInvalid, signal);
    }

    [Fact]
    public void Signal_ConfidenceDeflated_LowConfGoodOutcome()
    {
        var signal = OutcomeLearningService.DetermineRecalibrationSignal(
            0.30, OutcomeDirection.Overperformed, 50.0);
        Assert.Equal(RecalibrationSignal.ConfidenceDeflated, signal);
    }

    [Fact]
    public void Signal_ValueModelDrift_LargeVariance()
    {
        var signal = OutcomeLearningService.DetermineRecalibrationSignal(
            0.60, OutcomeDirection.Overperformed, 45.0);
        Assert.Equal(RecalibrationSignal.ValueModelDrift, signal);
    }

    [Fact]
    public void Signal_Calibrated_OnTarget()
    {
        var signal = OutcomeLearningService.DetermineRecalibrationSignal(
            0.80, OutcomeDirection.OnTarget, 5.0);
        Assert.Equal(RecalibrationSignal.ConfidenceCalibrated, signal);
    }

    // ── Tenant Isolation ──────────────────────────────────────

    [Fact]
    public async Task ListOutcomes_TenantIsolation()
    {
        var svc = CreateService();
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        await svc.RecordExpectedOutcomeAsync(Guid.NewGuid(), tenant1, "T1", 100m, 0.8, null, "admin");
        await svc.RecordExpectedOutcomeAsync(Guid.NewGuid(), tenant2, "T2", 200m, 0.9, null, "admin");
        await svc.RecordExpectedOutcomeAsync(Guid.NewGuid(), tenant1, "T1b", 300m, 0.7, null, "admin");

        var t1Outcomes = await svc.ListOutcomesAsync(tenant1);
        var t2Outcomes = await svc.ListOutcomesAsync(tenant2);

        Assert.Equal(2, t1Outcomes.Count);
        Assert.Single(t2Outcomes);
        Assert.All(t1Outcomes, o => Assert.Equal(tenant1, o.TenantId));
        Assert.All(t2Outcomes, o => Assert.Equal(tenant2, o.TenantId));
    }

    // ── Persistence / Retrieval ───────────────────────────────

    [Fact]
    public async Task GetOutcome_ReturnsStoredRecord()
    {
        var svc = CreateService();
        await svc.RecordExpectedOutcomeAsync(
            _decisionId, _tenantId, "Test", 1000m, 0.75, null, "admin");

        var retrieved = await svc.GetOutcomeAsync(_decisionId);
        Assert.NotNull(retrieved);
        Assert.Equal(_decisionId, retrieved.DecisionId);
        Assert.Equal(1000m, retrieved.ExpectedValue);
    }

    [Fact]
    public async Task GetOutcome_ReturnsNullForUnknown()
    {
        var svc = CreateService();
        var result = await svc.GetOutcomeAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetOutcome_ReturnsUpdatedAfterActual()
    {
        var svc = CreateService();
        await svc.RecordExpectedOutcomeAsync(
            _decisionId, _tenantId, "Expected", 5000m, 0.8, null, "admin");
        await svc.RecordActualOutcomeAsync(
            _decisionId, "Actual", 6000m, null, "Good result", "analyst");

        var result = await svc.GetOutcomeAsync(_decisionId);
        Assert.Equal("Actual", result!.ActualOutcomeSummary);
        Assert.Equal(6000m, result.ActualValue);
        Assert.Equal(1000m, result.ValueVariance);
        Assert.NotNull(result.OutcomeObservedAtUtc);
    }

    // ── Calibration Summary ───────────────────────────────────

    [Fact]
    public async Task CalibrationSummary_AggregatesCorrectly()
    {
        var svc = CreateService();
        var tenantId = Guid.NewGuid();

        // Decision 1: on target
        var d1 = Guid.NewGuid();
        await svc.RecordExpectedOutcomeAsync(d1, tenantId, "d1", 100m, 0.90, null, "admin");
        await svc.RecordActualOutcomeAsync(d1, "d1 actual", 105m, null, null, "analyst");

        // Decision 2: overperformed
        var d2 = Guid.NewGuid();
        await svc.RecordExpectedOutcomeAsync(d2, tenantId, "d2", 100m, 0.70, null, "admin");
        await svc.RecordActualOutcomeAsync(d2, "d2 actual", 200m, null, null, "analyst");

        // Decision 3: underperformed
        var d3 = Guid.NewGuid();
        await svc.RecordExpectedOutcomeAsync(d3, tenantId, "d3", 100m, 0.80, null, "admin");
        await svc.RecordActualOutcomeAsync(d3, "d3 actual", 50m, "Missed target", null, "analyst");

        var summary = await svc.GetCalibrationSummaryAsync(tenantId);

        Assert.Equal(3, summary.TotalOutcomes);
        Assert.Equal(1, summary.OnTarget);
        Assert.Equal(1, summary.Overperformed);
        Assert.Equal(1, summary.Underperformed);
        Assert.True(summary.HitRate > 0.5); // OnTarget + Overperformed = 2/3
    }
}

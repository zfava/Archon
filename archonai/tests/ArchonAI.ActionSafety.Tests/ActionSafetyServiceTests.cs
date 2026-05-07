using ArchonAI.Api.Security;
using ArchonAI.Governance;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.ActionSafety;
using ArchonAI.Core.Models.ProofAnalytics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArchonAI.ActionSafety.Tests;

public sealed class ActionSafetyServiceTests
{
    private readonly IProofAnalyticsService _proofAnalytics = Substitute.For<IProofAnalyticsService>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<ActionSafetyService> _logger = Substitute.For<ILogger<ActionSafetyService>>();

    private ActionSafetyService CreateService() =>
        new(_proofAnalytics, _eventBus, _logger);

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private GovernedActionRecord MakeAction(
        Guid tenantId, string actionType, ActionSafetyClassification classification,
        Guid? decisionId = null, Guid? workflowId = null) =>
        new(
            Id: Guid.NewGuid(),
            TenantId: tenantId,
            DecisionId: decisionId,
            WorkflowId: workflowId,
            ApprovalGateId: null,
            ActionType: actionType,
            Description: $"Test {actionType} action",
            SafetyClassification: classification,
            Status: GovernedActionStatus.Executed,
            ExecutedBy: "test-user",
            ExecutedAtUtc: DateTimeOffset.UtcNow,
            RollbackHistory: Array.Empty<RollbackAttempt>(),
            CompensationOutcome: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

    // ── Safe Rollback Behavior ────────────────────────────────

    [Fact]
    public async Task Rollback_ReversibleAction_Succeeds()
    {
        var svc = CreateService();
        var classification = await svc.GetClassificationAsync("strategy.override");
        var action = MakeAction(TenantA, "strategy.override", classification);
        var recorded = await svc.RecordActionAsync(action);

        var result = await svc.AttemptRollbackAsync(recorded.Id, "admin-user");

        result.Status.Should().Be(GovernedActionStatus.RolledBack);
        result.RollbackHistory.Should().HaveCount(1);
        result.RollbackHistory[0].Status.Should().Be(RollbackAttemptStatus.Succeeded);
    }

    [Fact]
    public async Task Rollback_CompensatableAction_AppliesCompensation()
    {
        var svc = CreateService();
        var classification = await svc.GetClassificationAsync("decision.execute");
        var action = MakeAction(TenantA, "decision.execute", classification);
        var recorded = await svc.RecordActionAsync(action);

        var result = await svc.AttemptRollbackAsync(recorded.Id, "admin-user");

        result.Status.Should().Be(GovernedActionStatus.CompensationApplied);
        result.CompensationOutcome.Should().NotBeNullOrEmpty();
        result.RollbackHistory.Should().HaveCount(1);
        result.RollbackHistory[0].Status.Should().Be(RollbackAttemptStatus.Succeeded);
    }

    [Fact]
    public async Task Rollback_AutomaticRollbackType_SucceedsWithinWindow()
    {
        var svc = CreateService();
        var classification = await svc.GetClassificationAsync("connector.disconnect");
        var action = MakeAction(TenantA, "connector.disconnect", classification);
        var recorded = await svc.RecordActionAsync(action);

        var result = await svc.AttemptRollbackAsync(recorded.Id, "admin-user");

        result.Status.Should().Be(GovernedActionStatus.RolledBack);
        result.RollbackHistory[0].Detail.Should().Contain("ManualTrigger");
    }

    // ── Blocked Rollback for Irreversible Actions ─────────────

    [Fact]
    public async Task Rollback_IrreversibleAction_IsBlocked()
    {
        var svc = CreateService();
        var classification = await svc.GetClassificationAsync("notification.send");
        var action = MakeAction(TenantA, "notification.send", classification);
        var recorded = await svc.RecordActionAsync(action);

        var result = await svc.AttemptRollbackAsync(recorded.Id, "admin-user");

        result.Status.Should().Be(GovernedActionStatus.Irreversible);
        result.RollbackHistory.Should().HaveCount(1);
        result.RollbackHistory[0].Status.Should().Be(RollbackAttemptStatus.Blocked);
        result.RollbackHistory[0].Error.Should().Contain("irreversible");
    }

    [Fact]
    public async Task Rollback_UnknownActionType_TreatedAsIrreversible()
    {
        var svc = CreateService();
        var classification = await svc.GetClassificationAsync("unknown.action.type");

        classification.Reversibility.Should().Be(ReversibilityLevel.Irreversible);
        classification.RollbackSupported.Should().BeFalse();
    }

    [Fact]
    public async Task Rollback_ExpiredWindow_IsBlocked()
    {
        var svc = CreateService();
        // Create a classification with a very short window
        var classification = new ActionSafetyClassification(
            Guid.NewGuid(), "test.short.window", ReversibilityLevel.Reversible,
            true, RollbackStrategy.Automatic, TimeSpan.FromMilliseconds(1),
            null, null, "test", DateTimeOffset.UtcNow);
        await svc.SetClassificationAsync(classification);

        var action = new GovernedActionRecord(
            Guid.NewGuid(), TenantA, null, null, null,
            "test.short.window", "Test short window action", classification,
            GovernedActionStatus.Executed, "test-user",
            DateTimeOffset.UtcNow.AddSeconds(-10), // Executed 10 seconds ago
            Array.Empty<RollbackAttempt>(), null, DateTimeOffset.UtcNow);
        var recorded = await svc.RecordActionAsync(action);

        var result = await svc.AttemptRollbackAsync(recorded.Id, "admin-user");

        result.Status.Should().Be(GovernedActionStatus.RollbackWindowExpired);
        result.RollbackHistory[0].Status.Should().Be(RollbackAttemptStatus.Blocked);
        result.RollbackHistory[0].Error.Should().Contain("expired");
    }

    // ── Tenant Isolation ──────────────────────────────────────

    [Fact]
    public async Task TenantIsolation_ActionsFromDifferentTenants_AreNotMixed()
    {
        var svc = CreateService();
        var classA = await svc.GetClassificationAsync("data.read");
        var classB = await svc.GetClassificationAsync("data.read");

        await svc.RecordActionAsync(MakeAction(TenantA, "data.read", classA));
        await svc.RecordActionAsync(MakeAction(TenantA, "data.read", classA));
        await svc.RecordActionAsync(MakeAction(TenantB, "data.read", classB));

        var actionsA = await svc.ListActionsAsync(TenantA);
        var actionsB = await svc.ListActionsAsync(TenantB);

        actionsA.Should().HaveCount(2);
        actionsB.Should().HaveCount(1);
    }

    [Fact]
    public async Task TenantIsolation_SummaryIsScoped()
    {
        var svc = CreateService();
        var cls = await svc.GetClassificationAsync("data.read");
        var clsIrr = await svc.GetClassificationAsync("notification.send");

        await svc.RecordActionAsync(MakeAction(TenantA, "data.read", cls));
        await svc.RecordActionAsync(MakeAction(TenantA, "notification.send", clsIrr));
        await svc.RecordActionAsync(MakeAction(TenantB, "data.read", cls));

        var summaryA = await svc.GetRollbackSummaryAsync(TenantA);
        var summaryB = await svc.GetRollbackSummaryAsync(TenantB);

        summaryA.TotalActions.Should().Be(2);
        summaryA.Irreversible.Should().Be(1);
        summaryB.TotalActions.Should().Be(1);
        summaryB.Irreversible.Should().Be(0);
    }

    // ── Action State Consistency ──────────────────────────────

    [Fact]
    public async Task RollbackEligible_StatusIsSetCorrectly()
    {
        var svc = CreateService();
        var cls = await svc.GetClassificationAsync("strategy.override");
        var action = MakeAction(TenantA, "strategy.override", cls);
        var recorded = await svc.RecordActionAsync(action);

        recorded.Status.Should().Be(GovernedActionStatus.RollbackEligible);
    }

    [Fact]
    public async Task IrreversibleAction_StatusIsSetCorrectly()
    {
        var svc = CreateService();
        var cls = await svc.GetClassificationAsync("connector.send");
        var action = MakeAction(TenantA, "connector.send", cls);
        var recorded = await svc.RecordActionAsync(action);

        recorded.Status.Should().Be(GovernedActionStatus.Irreversible);
    }

    [Fact]
    public async Task DoubleRollback_IsBlocked()
    {
        var svc = CreateService();
        var cls = await svc.GetClassificationAsync("strategy.override");
        var action = MakeAction(TenantA, "strategy.override", cls);
        var recorded = await svc.RecordActionAsync(action);

        await svc.AttemptRollbackAsync(recorded.Id, "admin-user");
        var secondAttempt = await svc.AttemptRollbackAsync(recorded.Id, "admin-user");

        secondAttempt.RollbackHistory.Should().HaveCount(2);
        secondAttempt.RollbackHistory[1].Status.Should().Be(RollbackAttemptStatus.Blocked);
        secondAttempt.RollbackHistory[1].Error.Should().Contain("already in state");
    }

    [Fact]
    public async Task Rollback_NonExistentAction_ThrowsKeyNotFound()
    {
        var svc = CreateService();

        var act = () => svc.AttemptRollbackAsync(Guid.NewGuid(), "admin-user");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Proof Analytics Integration ───────────────────────────

    [Fact]
    public async Task SuccessfulRollback_RecordsProofEvent()
    {
        _proofAnalytics.RecordEventAsync(Arg.Any<ProofEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ProofEvent>());

        var svc = CreateService();
        var cls = await svc.GetClassificationAsync("strategy.override");
        var decisionId = Guid.NewGuid();
        var action = MakeAction(TenantA, "strategy.override", cls, decisionId: decisionId);
        var recorded = await svc.RecordActionAsync(action);

        await svc.AttemptRollbackAsync(recorded.Id, "admin-user");

        await _proofAnalytics.Received(1).RecordEventAsync(
            Arg.Is<ProofEvent>(pe =>
                pe.EventType == ProofEventType.ReversalApplied &&
                pe.DecisionId == decisionId &&
                pe.IsSuccess == true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordAction_PublishesSystemEvent()
    {
        var svc = CreateService();
        var cls = await svc.GetClassificationAsync("data.read");
        var action = MakeAction(TenantA, "data.read", cls);

        await svc.RecordActionAsync(action);

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(se => se.EventType == "action.safety.recorded"),
            Arg.Any<CancellationToken>());
    }

    // ── Classification Management ─────────────────────────────

    [Fact]
    public async Task SetClassification_OverridesExisting()
    {
        var svc = CreateService();
        var before = await svc.GetClassificationAsync("data.read");
        before.Reversibility.Should().Be(ReversibilityLevel.Reversible);

        var updated = before with { Reversibility = ReversibilityLevel.Compensatable };
        await svc.SetClassificationAsync(updated);

        var after = await svc.GetClassificationAsync("data.read");
        after.Reversibility.Should().Be(ReversibilityLevel.Compensatable);
    }

    [Fact]
    public async Task ListClassifications_ReturnsSeededDefaults()
    {
        var svc = CreateService();
        var list = await svc.ListClassificationsAsync();

        list.Should().NotBeEmpty();
        list.Should().Contain(c => c.ActionType == "notification.send" && c.Reversibility == ReversibilityLevel.Irreversible);
        list.Should().Contain(c => c.ActionType == "data.read" && c.Reversibility == ReversibilityLevel.Reversible);
        list.Should().Contain(c => c.ActionType == "decision.execute" && c.Reversibility == ReversibilityLevel.Compensatable);
    }
}

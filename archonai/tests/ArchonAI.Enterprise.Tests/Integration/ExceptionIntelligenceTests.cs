using ArchonAI.Api.Security;
using ArchonAI.Core.Services;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.ExceptionIntelligence;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Integration;

public sealed class ExceptionIntelligenceTests
{
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<ExceptionIntelligenceService> _logger = Substitute.For<ILogger<ExceptionIntelligenceService>>();

    private ExceptionIntelligenceService CreateService() => new(_eventBus, _logger);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();

    private OperationalException MakeException(
        ExceptionCategory category = ExceptionCategory.Anomaly,
        ExceptionSeverity severity = ExceptionSeverity.High,
        string title = "Test Exception",
        ExceptionStatus status = ExceptionStatus.Open,
        double urgency = 0.8,
        double economicImpact = 50000,
        double confidence = 0.7,
        EscalationLevel escalation = EscalationLevel.None,
        Guid? tenantId = null,
        string? assignedTo = null,
        IReadOnlyList<ExceptionArtifactLink>? links = null,
        RecommendedAction? action = null) =>
        new(
            Id: Guid.NewGuid(),
            TenantId: tenantId ?? _tenantId,
            Category: category,
            Severity: severity,
            Title: title,
            Description: $"{title} description",
            Domain: "Operations",
            Status: status,
            Urgency: urgency,
            EconomicImpactEstimate: economicImpact,
            Confidence: confidence,
            EscalationLevel: escalation,
            AssignedTo: assignedTo,
            EscalationPath: null,
            LinkedArtifacts: links ?? Array.Empty<ExceptionArtifactLink>(),
            RecommendedAction: action,
            CreatedBy: "test",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            AcknowledgedAtUtc: null,
            ResolvedAtUtc: null);

    // ── Raise & Persistence ────────────────────────────────────

    [Fact]
    public async Task RaiseException_StoresAndReturns()
    {
        var svc = CreateService();
        var ex = MakeException();

        var result = await svc.RaiseExceptionAsync(ex);

        Assert.Equal(ex.Id, result.Id);
        Assert.Equal(ex.Title, result.Title);
    }

    [Fact]
    public async Task RaiseException_PublishesEvent()
    {
        var svc = CreateService();
        await svc.RaiseExceptionAsync(MakeException());

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "exception.raised"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetException_ReturnsStored()
    {
        var svc = CreateService();
        var ex = MakeException();
        await svc.RaiseExceptionAsync(ex);

        var result = await svc.GetExceptionAsync(ex.Id, _tenantId);

        Assert.NotNull(result);
        Assert.Equal(ex.Title, result!.Title);
    }

    [Fact]
    public async Task GetException_NonExistent_ReturnsNull()
    {
        var svc = CreateService();
        Assert.Null(await svc.GetExceptionAsync(Guid.NewGuid(), _tenantId));
    }

    // ── Tenant Isolation ───────────────────────────────────────

    [Fact]
    public async Task GetException_WrongTenant_ReturnsNull()
    {
        var svc = CreateService();
        var ex = MakeException();
        await svc.RaiseExceptionAsync(ex);

        Assert.Null(await svc.GetExceptionAsync(ex.Id, _otherTenantId));
    }

    [Fact]
    public async Task ListExceptions_FiltersByTenant()
    {
        var svc = CreateService();
        await svc.RaiseExceptionAsync(MakeException(title: "Mine"));
        await svc.RaiseExceptionAsync(MakeException(title: "Other", tenantId: _otherTenantId));

        var results = await svc.ListExceptionsAsync(_tenantId);

        Assert.Single(results);
        Assert.Equal("Mine", results[0].Title);
    }

    // ── Filtering ──────────────────────────────────────────────

    [Fact]
    public async Task ListExceptions_FiltersBySeverity()
    {
        var svc = CreateService();
        await svc.RaiseExceptionAsync(MakeException(severity: ExceptionSeverity.Critical, title: "Crit"));
        await svc.RaiseExceptionAsync(MakeException(severity: ExceptionSeverity.Info, title: "Info"));

        var results = await svc.ListExceptionsAsync(_tenantId, severity: ExceptionSeverity.Critical);

        Assert.Single(results);
        Assert.Equal("Crit", results[0].Title);
    }

    [Fact]
    public async Task ListExceptions_FiltersByCategory()
    {
        var svc = CreateService();
        await svc.RaiseExceptionAsync(MakeException(category: ExceptionCategory.Failure, title: "Fail"));
        await svc.RaiseExceptionAsync(MakeException(category: ExceptionCategory.Drift, title: "Drift"));

        var results = await svc.ListExceptionsAsync(_tenantId, category: ExceptionCategory.Drift);

        Assert.Single(results);
        Assert.Equal("Drift", results[0].Title);
    }

    [Fact]
    public async Task ListExceptions_FiltersByStatus()
    {
        var svc = CreateService();
        await svc.RaiseExceptionAsync(MakeException(status: ExceptionStatus.Open, title: "Open"));
        await svc.RaiseExceptionAsync(MakeException(status: ExceptionStatus.Resolved, title: "Resolved"));

        var results = await svc.ListExceptionsAsync(_tenantId, status: ExceptionStatus.Open);

        Assert.Single(results);
        Assert.Equal("Open", results[0].Title);
    }

    [Fact]
    public async Task ListExceptions_FiltersByDomain()
    {
        var svc = CreateService();
        await svc.RaiseExceptionAsync(MakeException(title: "Ops"));

        var results = await svc.ListExceptionsAsync(_tenantId, domain: "Operations");
        Assert.Single(results);

        var empty = await svc.ListExceptionsAsync(_tenantId, domain: "Finance");
        Assert.Empty(empty);
    }

    // ── Status / Assignment ────────────────────────────────────

    [Fact]
    public async Task UpdateStatus_ChangesStatus()
    {
        var svc = CreateService();
        var ex = MakeException();
        await svc.RaiseExceptionAsync(ex);

        var updated = await svc.UpdateStatusAsync(ex.Id, _tenantId, ExceptionStatus.Acknowledged);

        Assert.NotNull(updated);
        Assert.Equal(ExceptionStatus.Acknowledged, updated!.Status);
        Assert.NotNull(updated.AcknowledgedAtUtc);
    }

    [Fact]
    public async Task UpdateStatus_SetsAssignment()
    {
        var svc = CreateService();
        var ex = MakeException();
        await svc.RaiseExceptionAsync(ex);

        var updated = await svc.UpdateStatusAsync(ex.Id, _tenantId, ExceptionStatus.InProgress, "alice");

        Assert.Equal("alice", updated!.AssignedTo);
    }

    [Fact]
    public async Task UpdateStatus_Resolve_SetsResolvedTimestamp()
    {
        var svc = CreateService();
        var ex = MakeException();
        await svc.RaiseExceptionAsync(ex);

        var updated = await svc.UpdateStatusAsync(ex.Id, _tenantId, ExceptionStatus.Resolved);

        Assert.NotNull(updated!.ResolvedAtUtc);
    }

    [Fact]
    public async Task UpdateStatus_WrongTenant_ReturnsNull()
    {
        var svc = CreateService();
        var ex = MakeException();
        await svc.RaiseExceptionAsync(ex);

        Assert.Null(await svc.UpdateStatusAsync(ex.Id, _otherTenantId, ExceptionStatus.Resolved));
    }

    // ── Recommended Action ─────────────────────────────────────

    [Fact]
    public async Task SetRecommendedAction_AttachesAction()
    {
        var svc = CreateService();
        var ex = MakeException();
        await svc.RaiseExceptionAsync(ex);

        var action = new RecommendedAction("Restart", "Restart the failing service", "System", "sys-001", "High");
        var updated = await svc.SetRecommendedActionAsync(ex.Id, _tenantId, action);

        Assert.NotNull(updated!.RecommendedAction);
        Assert.Equal("Restart", updated.RecommendedAction!.ActionType);
    }

    [Fact]
    public async Task SetRecommendedAction_WrongTenant_ReturnsNull()
    {
        var svc = CreateService();
        var ex = MakeException();
        await svc.RaiseExceptionAsync(ex);

        var action = new RecommendedAction("Fix", "Fix it", null, null, "Medium");
        Assert.Null(await svc.SetRecommendedActionAsync(ex.Id, _otherTenantId, action));
    }

    // ── Linkage ────────────────────────────────────────────────

    [Fact]
    public async Task Exception_PreservesLinkedArtifacts()
    {
        var svc = CreateService();
        var links = new List<ExceptionArtifactLink>
        {
            new("Decision", Guid.NewGuid().ToString(), "pricing decision"),
            new("TwinEntity", Guid.NewGuid().ToString(), "order-system"),
            new("Workflow", Guid.NewGuid().ToString(), null),
        };
        var ex = MakeException(links: links);
        await svc.RaiseExceptionAsync(ex);

        var result = await svc.GetExceptionAsync(ex.Id, _tenantId);

        Assert.Equal(3, result!.LinkedArtifacts.Count);
        Assert.Contains(result.LinkedArtifacts, l => l.ArtifactType == "Decision");
        Assert.Contains(result.LinkedArtifacts, l => l.ArtifactType == "TwinEntity");
        Assert.Contains(result.LinkedArtifacts, l => l.ArtifactType == "Workflow");
    }

    // ── Prioritization ─────────────────────────────────────────

    [Fact]
    public async Task ListExceptions_OrderedByPriority()
    {
        var svc = CreateService();
        await svc.RaiseExceptionAsync(MakeException(
            severity: ExceptionSeverity.Info, urgency: 0.2, economicImpact: 100,
            confidence: 0.3, title: "Low priority"));
        await svc.RaiseExceptionAsync(MakeException(
            severity: ExceptionSeverity.Critical, urgency: 0.9, economicImpact: 500000,
            confidence: 0.9, title: "High priority"));

        var results = await svc.ListExceptionsAsync(_tenantId);

        Assert.Equal("High priority", results[0].Title);
        Assert.Equal("Low priority", results[1].Title);
    }

    [Fact]
    public async Task GetPrioritizedQueue_ReturnsScoresDescending()
    {
        var svc = CreateService();
        await svc.RaiseExceptionAsync(MakeException(
            severity: ExceptionSeverity.Warning, urgency: 0.5, economicImpact: 1000,
            confidence: 0.5, title: "Medium"));
        await svc.RaiseExceptionAsync(MakeException(
            severity: ExceptionSeverity.Critical, urgency: 1.0, economicImpact: 1000000,
            confidence: 0.9, escalation: EscalationLevel.Executive, title: "Top"));

        var queue = await svc.GetPrioritizedQueueAsync(_tenantId);

        Assert.Equal(2, queue.Count);
        Assert.True(queue[0].Score > queue[1].Score);
    }

    [Fact]
    public async Task GetPrioritizedQueue_ExcludesResolvedAndDismissed()
    {
        var svc = CreateService();
        await svc.RaiseExceptionAsync(MakeException(title: "Open"));
        await svc.RaiseExceptionAsync(MakeException(status: ExceptionStatus.Resolved, title: "Done"));
        await svc.RaiseExceptionAsync(MakeException(status: ExceptionStatus.Dismissed, title: "Gone"));

        var queue = await svc.GetPrioritizedQueueAsync(_tenantId);

        Assert.Single(queue);
    }

    [Fact]
    public async Task GetPrioritizedQueue_RespectsLimit()
    {
        var svc = CreateService();
        for (int i = 0; i < 5; i++)
            await svc.RaiseExceptionAsync(MakeException(title: $"Ex {i}"));

        var queue = await svc.GetPrioritizedQueueAsync(_tenantId, limit: 3);

        Assert.Equal(3, queue.Count);
    }

    [Fact]
    public async Task GetPrioritizedQueue_TenantIsolation()
    {
        var svc = CreateService();
        await svc.RaiseExceptionAsync(MakeException(title: "Mine"));
        await svc.RaiseExceptionAsync(MakeException(title: "Other", tenantId: _otherTenantId));

        var queue = await svc.GetPrioritizedQueueAsync(_tenantId);

        Assert.Single(queue);
    }

    // ── Queue Summary ──────────────────────────────────────────

    [Fact]
    public async Task GetQueueSummary_AggregatesCorrectly()
    {
        var svc = CreateService();
        await svc.RaiseExceptionAsync(MakeException(
            severity: ExceptionSeverity.Critical, economicImpact: 100000,
            category: ExceptionCategory.Failure));
        await svc.RaiseExceptionAsync(MakeException(
            severity: ExceptionSeverity.High, economicImpact: 25000,
            category: ExceptionCategory.Drift));
        await svc.RaiseExceptionAsync(MakeException(
            severity: ExceptionSeverity.Warning, economicImpact: 5000,
            category: ExceptionCategory.Drift));
        await svc.RaiseExceptionAsync(MakeException(
            status: ExceptionStatus.Resolved, economicImpact: 999999));

        var summary = await svc.GetQueueSummaryAsync(_tenantId);

        Assert.Equal(3, summary.TotalOpen);
        Assert.Equal(1, summary.Critical);
        Assert.Equal(1, summary.High);
        Assert.Equal(1, summary.Warning);
        Assert.Equal(130000, summary.TotalEconomicExposure);
        Assert.Equal(1, summary.ByCategory["Failure"]);
        Assert.Equal(2, summary.ByCategory["Drift"]);
    }

    [Fact]
    public async Task GetQueueSummary_TenantIsolation()
    {
        var svc = CreateService();
        await svc.RaiseExceptionAsync(MakeException(economicImpact: 10000));
        await svc.RaiseExceptionAsync(MakeException(economicImpact: 99999, tenantId: _otherTenantId));

        var summary = await svc.GetQueueSummaryAsync(_tenantId);

        Assert.Equal(1, summary.TotalOpen);
        Assert.Equal(10000, summary.TotalEconomicExposure);
    }

    // ── Priority scoring logic ─────────────────────────────────

    [Fact]
    public void PriorityScore_CriticalHigherThanInfo()
    {
        var critical = MakeException(severity: ExceptionSeverity.Critical, urgency: 0.5, economicImpact: 1000, confidence: 0.5);
        var info = MakeException(severity: ExceptionSeverity.Info, urgency: 0.5, economicImpact: 1000, confidence: 0.5);

        Assert.True(ExceptionIntelligenceService.ComputePriorityScore(critical) >
                    ExceptionIntelligenceService.ComputePriorityScore(info));
    }

    [Fact]
    public void PriorityScore_HigherUrgencyScoresMore()
    {
        var high = MakeException(urgency: 1.0, economicImpact: 1000, confidence: 0.5);
        var low = MakeException(urgency: 0.1, economicImpact: 1000, confidence: 0.5);

        Assert.True(ExceptionIntelligenceService.ComputePriorityScore(high) >
                    ExceptionIntelligenceService.ComputePriorityScore(low));
    }

    [Fact]
    public void PriorityScore_ExecutiveEscalationBoosts()
    {
        var escalated = MakeException(escalation: EscalationLevel.Executive);
        var normal = MakeException(escalation: EscalationLevel.None);

        Assert.True(ExceptionIntelligenceService.ComputePriorityScore(escalated) >
                    ExceptionIntelligenceService.ComputePriorityScore(normal));
    }

    [Fact]
    public void PriorityScore_HigherEconomicImpactScoresMore()
    {
        var expensive = MakeException(economicImpact: 1000000);
        var cheap = MakeException(economicImpact: 100);

        Assert.True(ExceptionIntelligenceService.ComputePriorityScore(expensive) >
                    ExceptionIntelligenceService.ComputePriorityScore(cheap));
    }
}

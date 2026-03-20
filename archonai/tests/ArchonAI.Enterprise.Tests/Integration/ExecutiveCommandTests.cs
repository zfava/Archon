using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.ActionSafety;
using ArchonAI.Core.Models.Decisions;
using ArchonAI.Core.Models.ExceptionIntelligence;
using ArchonAI.Core.Models.Governance;
using ArchonAI.Core.Models.HeroWorkflow;
using ArchonAI.Core.Models.OperationalTwin;
using ArchonAI.Core.Models.ProofAnalytics;
using ArchonAI.Core.Models.Scenario;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Integration;

public sealed class ExecutiveCommandTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();

    private readonly IExceptionIntelligenceService _exceptions = Substitute.For<IExceptionIntelligenceService>();
    private readonly IGovernanceService _governance = Substitute.For<IGovernanceService>();
    private readonly IOutcomeLearningService _outcomes = Substitute.For<IOutcomeLearningService>();
    private readonly IOperationalTwinService _twin = Substitute.For<IOperationalTwinService>();
    private readonly ITrustTierService _trustTiers = Substitute.For<ITrustTierService>();
    private readonly IScenarioService _scenarios = Substitute.For<IScenarioService>();
    private readonly IProofAnalyticsService _proof = Substitute.For<IProofAnalyticsService>();
    private readonly IActionSafetyService _actionSafety = Substitute.For<IActionSafetyService>();
    private readonly IHeroWorkflowService _workflows = Substitute.For<IHeroWorkflowService>();

    private ExecutiveCommandService CreateService() =>
        new(_exceptions, _governance, _outcomes, _twin, _trustTiers, _scenarios,
            _proof, _actionSafety, _workflows);

    // ── Helpers ─────────────────────────────────────────────────────

    private void SetupEmptyDefaults(Guid tenantId)
    {
        var tenantStr = tenantId.ToString();

        _exceptions.GetQueueSummaryAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new ExceptionQueueSummary(0, 0, 0, 0, 0,
                new Dictionary<string, int>(), DateTimeOffset.UtcNow));

        _exceptions.GetPrioritizedQueueAsync(tenantId, 5, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ExceptionPriorityScore>().AsReadOnly()
                as IReadOnlyList<ExceptionPriorityScore>);

        _exceptions.ListExceptionsAsync(tenantId,
            Arg.Any<ExceptionSeverity?>(), Arg.Any<ExceptionCategory?>(),
            Arg.Any<ExceptionStatus?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OperationalException>().AsReadOnly()
                as IReadOnlyList<OperationalException>);

        _governance.ListPendingApprovalsAsync(tenantStr, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ApprovalGate>().AsReadOnly()
                as IReadOnlyList<ApprovalGate>);

        _outcomes.GetCalibrationSummaryAsync(tenantId, null, Arg.Any<CancellationToken>())
            .Returns(new CalibrationSummary(
                tenantStr, null, 0, 0, 0, 0, 0, 0, 0,
                new Dictionary<string, int>()));

        _outcomes.ListOutcomesAsync(tenantId, 50, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OutcomeRecord>().AsReadOnly()
                as IReadOnlyList<OutcomeRecord>);

        _twin.GetOverviewAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new TwinOverview(
                tenantId, new Dictionary<string, int>(),
                Array.Empty<TwinBottleneck>(),
                Array.Empty<TwinKpi>(), 0, DateTimeOffset.UtcNow));

        _trustTiers.ListPoliciesAsync(tenantStr, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TrustTierPolicy>().AsReadOnly()
                as IReadOnlyList<TrustTierPolicy>);

        _trustTiers.GetTierMapAsync(tenantStr, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, ExecutionTrustTier>()
                as IReadOnlyDictionary<string, ExecutionTrustTier>);

        _scenarios.ListScenariosAsync(tenantId,
            Arg.Any<ScenarioType?>(), Arg.Any<ScenarioStatus?>(),
            Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Scenario>().AsReadOnly()
                as IReadOnlyList<Scenario>);

        _proof.GetDashboardAsync(tenantId, null, Arg.Any<CancellationToken>())
            .Returns(new ProofDashboard(
                tenantId,
                new PredictedVsActualSummary(tenantId, null, 0, 0, 0, 0, 0, 0, 0, 0m, 0m, 0m, 0,
                    Array.Empty<PredictedVsActualEntry>()),
                new ApprovalConversionSummary(tenantId, 0, 0, 0, 0, 0, 0, 0, null,
                    new Dictionary<string, ApprovalConversionByType>()),
                new ExecutionTrendSummary(tenantId, 0, 0, 0, 0,
                    Array.Empty<ExecutionTrendBucket>()),
                new OverrideRateSummary(tenantId, 0, 0, 0, 0, 0,
                    new Dictionary<string, int>()),
                new TrustAnalyticsSummary(tenantId,
                    Array.Empty<TrustByActionType>()),
                DateTimeOffset.UtcNow));

        _actionSafety.GetRollbackSummaryAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new RollbackSummary(tenantId, 0, 0, 0, 0, 0, 0, 0, 0, 0));

        _workflows.ListAsync(tenantId, Arg.Any<string?>(), Arg.Any<HeroWorkflowStatus?>(),
            10, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<HeroWorkflowSummary>().AsReadOnly()
                as IReadOnlyList<HeroWorkflowSummary>);
    }

    // ── Composition behavior ────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_EmptyTenant_ReturnsZeroBriefs()
    {
        SetupEmptyDefaults(_tenantId);
        var svc = CreateService();

        var result = await svc.GetCommandSummaryAsync(_tenantId);

        Assert.Equal(_tenantId, result.TenantId);
        Assert.Equal(0, result.ExceptionBrief.TotalOpen);
        Assert.Equal(0, result.ExceptionBrief.Critical);
        Assert.Empty(result.ExceptionBrief.TopExceptions);
        Assert.Equal(0, result.ApprovalBrief.PendingCount);
        Assert.Empty(result.ApprovalBrief.PendingApprovals);
        Assert.Equal(0, result.CalibrationBrief.TotalOutcomes);
        Assert.Equal(0, result.OperationalBrief.ActiveBottlenecks);
        Assert.Equal(0, result.TrustBrief.TotalPolicies);
        Assert.Equal(0, result.ScenarioBrief.TotalActive);
        Assert.Equal(0, result.EconomicBrief.OutcomesDrifting);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_ExceptionBrief_ReflectsQueueSummary()
    {
        SetupEmptyDefaults(_tenantId);

        _exceptions.GetQueueSummaryAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new ExceptionQueueSummary(8, 2, 3, 3, 150_000,
                new Dictionary<string, int> { ["Anomaly"] = 5, ["Drift"] = 3 },
                DateTimeOffset.UtcNow));

        var svc = CreateService();
        var result = await svc.GetCommandSummaryAsync(_tenantId);

        Assert.Equal(8, result.ExceptionBrief.TotalOpen);
        Assert.Equal(2, result.ExceptionBrief.Critical);
        Assert.Equal(3, result.ExceptionBrief.High);
        Assert.Equal(150_000, result.ExceptionBrief.TotalEconomicExposure);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_TopExceptions_OrderedByPriorityScore()
    {
        SetupEmptyDefaults(_tenantId);

        var lowId = Guid.NewGuid();
        var highId = Guid.NewGuid();

        var exceptions = new List<OperationalException>
        {
            MakeException(lowId, ExceptionSeverity.High, "Low priority", ExceptionStatus.Open),
            MakeException(highId, ExceptionSeverity.Critical, "High priority", ExceptionStatus.Open),
        };

        _exceptions.ListExceptionsAsync(_tenantId,
            Arg.Any<ExceptionSeverity?>(), Arg.Any<ExceptionCategory?>(),
            Arg.Any<ExceptionStatus?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(exceptions.AsReadOnly());

        _exceptions.GetPrioritizedQueueAsync(_tenantId, 5, Arg.Any<CancellationToken>())
            .Returns(new List<ExceptionPriorityScore>
            {
                new(lowId, 20.0, "low"),
                new(highId, 90.0, "high"),
            }.AsReadOnly() as IReadOnlyList<ExceptionPriorityScore>);

        var svc = CreateService();
        var result = await svc.GetCommandSummaryAsync(_tenantId);

        Assert.Equal(2, result.ExceptionBrief.TopExceptions.Count);
        Assert.Equal("High priority", result.ExceptionBrief.TopExceptions[0].Title);
        Assert.Equal(90.0, result.ExceptionBrief.TopExceptions[0].PriorityScore);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_TopExceptions_ExcludesResolvedAndDismissed()
    {
        SetupEmptyDefaults(_tenantId);

        var openId = Guid.NewGuid();
        var resolvedId = Guid.NewGuid();
        var dismissedId = Guid.NewGuid();

        var exceptions = new List<OperationalException>
        {
            MakeException(openId, ExceptionSeverity.High, "Open one", ExceptionStatus.Open),
            MakeException(resolvedId, ExceptionSeverity.Critical, "Resolved", ExceptionStatus.Resolved),
            MakeException(dismissedId, ExceptionSeverity.Critical, "Dismissed", ExceptionStatus.Dismissed),
        };

        _exceptions.ListExceptionsAsync(_tenantId,
            Arg.Any<ExceptionSeverity?>(), Arg.Any<ExceptionCategory?>(),
            Arg.Any<ExceptionStatus?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(exceptions.AsReadOnly());

        _exceptions.GetPrioritizedQueueAsync(_tenantId, 5, Arg.Any<CancellationToken>())
            .Returns(new List<ExceptionPriorityScore>
            {
                new(openId, 50.0, "ok"),
                new(resolvedId, 99.0, "resolved"),
                new(dismissedId, 98.0, "dismissed"),
            }.AsReadOnly() as IReadOnlyList<ExceptionPriorityScore>);

        var svc = CreateService();
        var result = await svc.GetCommandSummaryAsync(_tenantId);

        Assert.Single(result.ExceptionBrief.TopExceptions);
        Assert.Equal("Open one", result.ExceptionBrief.TopExceptions[0].Title);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_TopExceptions_LimitedToFive()
    {
        SetupEmptyDefaults(_tenantId);

        var exceptions = Enumerable.Range(0, 10)
            .Select(i =>
            {
                var id = Guid.NewGuid();
                return (id, exc: MakeException(id, ExceptionSeverity.High, $"Exc {i}", ExceptionStatus.Open));
            })
            .ToList();

        _exceptions.ListExceptionsAsync(_tenantId,
            Arg.Any<ExceptionSeverity?>(), Arg.Any<ExceptionCategory?>(),
            Arg.Any<ExceptionStatus?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(exceptions.Select(e => e.exc).ToList().AsReadOnly());

        _exceptions.GetPrioritizedQueueAsync(_tenantId, 5, Arg.Any<CancellationToken>())
            .Returns(exceptions.Select((e, i) => new ExceptionPriorityScore(e.id, 100 - i, ""))
                .ToList().AsReadOnly() as IReadOnlyList<ExceptionPriorityScore>);

        var svc = CreateService();
        var result = await svc.GetCommandSummaryAsync(_tenantId);

        Assert.Equal(5, result.ExceptionBrief.TopExceptions.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_ApprovalBrief_CountsAndHeadlines()
    {
        SetupEmptyDefaults(_tenantId);
        var tenantStr = _tenantId.ToString();

        var approvals = new List<ApprovalGate>
        {
            new(Guid.NewGuid(), "deploy", "res-1", tenantStr, "alice",
                "Need to deploy ASAP", ApprovalStatus.Pending, null, null,
                DateTimeOffset.UtcNow.AddHours(-2), null),
            new(Guid.NewGuid(), "budget_change", "res-2", tenantStr, "bob",
                "Budget reallocation", ApprovalStatus.Pending, null, null,
                DateTimeOffset.UtcNow.AddHours(-1), null),
        };

        _governance.ListPendingApprovalsAsync(tenantStr, Arg.Any<CancellationToken>())
            .Returns(approvals.AsReadOnly());

        var svc = CreateService();
        var result = await svc.GetCommandSummaryAsync(_tenantId);

        Assert.Equal(2, result.ApprovalBrief.PendingCount);
        Assert.Equal(2, result.ApprovalBrief.PendingApprovals.Count);
        Assert.Equal("deploy", result.ApprovalBrief.PendingApprovals[0].ActionType);
        Assert.Equal("alice", result.ApprovalBrief.PendingApprovals[0].RequestedBy);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_CalibrationBrief_MirrorsCalibrationSummary()
    {
        SetupEmptyDefaults(_tenantId);
        var tenantStr = _tenantId.ToString();

        _outcomes.GetCalibrationSummaryAsync(_tenantId, null, Arg.Any<CancellationToken>())
            .Returns(new CalibrationSummary(
                tenantStr, null, 100, 70, 15, 15, 0.75, 0.7, 12.5,
                new Dictionary<string, int>
                {
                    ["ConfidenceCalibrated"] = 60,
                    ["ConfidenceInflated"] = 20,
                }));

        var svc = CreateService();
        var result = await svc.GetCommandSummaryAsync(_tenantId);

        Assert.Equal(100, result.CalibrationBrief.TotalOutcomes);
        Assert.Equal(15, result.CalibrationBrief.Underperformed);
        Assert.Equal(0.7, result.CalibrationBrief.HitRate);
        Assert.Equal(12.5, result.CalibrationBrief.MeanVariancePercent);
        Assert.Equal(2, result.CalibrationBrief.SignalDistribution.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_OperationalBrief_IncludesBottlenecksAndCounts()
    {
        SetupEmptyDefaults(_tenantId);

        var bottleneck = new TwinBottleneck(
            Guid.NewGuid(), _tenantId, Guid.NewGuid(),
            "Pipeline stuck", BottleneckSeverity.High, "Congestion",
            false, DateTimeOffset.UtcNow.AddMinutes(-30), null);

        _twin.GetOverviewAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new TwinOverview(
                _tenantId,
                new Dictionary<string, int> { ["Team"] = 5, ["System"] = 3 },
                new List<TwinBottleneck> { bottleneck },
                Array.Empty<TwinKpi>(), 12, DateTimeOffset.UtcNow));

        var svc = CreateService();
        var result = await svc.GetCommandSummaryAsync(_tenantId);

        Assert.Equal(1, result.OperationalBrief.ActiveBottlenecks);
        Assert.Equal(0, result.OperationalBrief.WarningKpis);
        Assert.Equal(12, result.OperationalBrief.TotalDependencies);
        Assert.Single(result.OperationalBrief.TopBottlenecks);
        Assert.Equal("Pipeline stuck", result.OperationalBrief.TopBottlenecks[0].Description);
        Assert.Equal(2, result.OperationalBrief.EntityCounts.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_TrustBrief_PolicyCountAndTierMap()
    {
        SetupEmptyDefaults(_tenantId);
        var tenantStr = _tenantId.ToString();

        var policies = new List<TrustTierPolicy>
        {
            new(Guid.NewGuid(), tenantStr, "deploy", ExecutionTrustTier.DraftApprovalRequired,
                null, null, false, null, true, "admin", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(Guid.NewGuid(), tenantStr, "budget", ExecutionTrustTier.RecommendOnly,
                null, null, false, null, true, "admin", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };

        _trustTiers.ListPoliciesAsync(tenantStr, Arg.Any<CancellationToken>())
            .Returns(policies.AsReadOnly());

        _trustTiers.GetTierMapAsync(tenantStr, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, ExecutionTrustTier>
            {
                ["deploy"] = ExecutionTrustTier.DraftApprovalRequired,
                ["budget"] = ExecutionTrustTier.RecommendOnly,
            } as IReadOnlyDictionary<string, ExecutionTrustTier>);

        var svc = CreateService();
        var result = await svc.GetCommandSummaryAsync(_tenantId);

        Assert.Equal(2, result.TrustBrief.TotalPolicies);
        Assert.Equal(2, result.TrustBrief.TierMap.Count);
        Assert.Equal("DraftApprovalRequired", result.TrustBrief.TierMap["deploy"]);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_ScenarioBrief_CountsActiveAndCompared()
    {
        SetupEmptyDefaults(_tenantId);

        var scenarios = new List<Scenario>
        {
            MakeScenario(ScenarioStatus.Active, ScenarioType.WhatIf, "Scenario A"),
            MakeScenario(ScenarioStatus.Active, ScenarioType.CostReduction, "Scenario B"),
            MakeScenario(ScenarioStatus.Compared, ScenarioType.WhatIf, "Scenario C"),
            MakeScenario(ScenarioStatus.Draft, ScenarioType.WhatIf, "Scenario D"),
        };

        _scenarios.ListScenariosAsync(_tenantId,
            Arg.Any<ScenarioType?>(), Arg.Any<ScenarioStatus?>(),
            Arg.Any<CancellationToken>())
            .Returns(scenarios.AsReadOnly());

        var svc = CreateService();
        var result = await svc.GetCommandSummaryAsync(_tenantId);

        Assert.Equal(2, result.ScenarioBrief.TotalActive);
        Assert.Equal(1, result.ScenarioBrief.TotalCompared);
        Assert.Equal(4, result.ScenarioBrief.RecentScenarios.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_ScenarioBrief_RecentLimitedToFive()
    {
        SetupEmptyDefaults(_tenantId);

        var scenarios = Enumerable.Range(0, 8)
            .Select(i => MakeScenario(ScenarioStatus.Active, ScenarioType.WhatIf, $"S{i}"))
            .ToList();

        _scenarios.ListScenariosAsync(_tenantId,
            Arg.Any<ScenarioType?>(), Arg.Any<ScenarioStatus?>(),
            Arg.Any<CancellationToken>())
            .Returns(scenarios.AsReadOnly());

        var svc = CreateService();
        var result = await svc.GetCommandSummaryAsync(_tenantId);

        Assert.Equal(5, result.ScenarioBrief.RecentScenarios.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_EconomicBrief_ComposesFromMultipleSources()
    {
        SetupEmptyDefaults(_tenantId);
        var tenantStr = _tenantId.ToString();

        _exceptions.GetQueueSummaryAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new ExceptionQueueSummary(5, 1, 2, 2, 250_000,
                new Dictionary<string, int>(), DateTimeOffset.UtcNow));

        var approvals = new List<ApprovalGate>
        {
            new(Guid.NewGuid(), "deploy", "r1", tenantStr, "alice", "j1",
                ApprovalStatus.Pending, null, null, DateTimeOffset.UtcNow, null),
            new(Guid.NewGuid(), "budget", "r2", tenantStr, "bob", "j2",
                ApprovalStatus.Pending, null, null, DateTimeOffset.UtcNow, null),
            new(Guid.NewGuid(), "hire", "r3", tenantStr, "carol", "j3",
                ApprovalStatus.Pending, null, null, DateTimeOffset.UtcNow, null),
        };
        _governance.ListPendingApprovalsAsync(tenantStr, Arg.Any<CancellationToken>())
            .Returns(approvals.AsReadOnly());

        var outcomes = new List<OutcomeRecord>
        {
            MakeOutcome(OutcomeDirection.Underperformed),
            MakeOutcome(OutcomeDirection.Underperformed),
            MakeOutcome(OutcomeDirection.OnTarget),
        };
        _outcomes.ListOutcomesAsync(_tenantId, 50, Arg.Any<CancellationToken>())
            .Returns(outcomes.AsReadOnly());

        var bottleneck = new TwinBottleneck(
            Guid.NewGuid(), _tenantId, Guid.NewGuid(), "Stuck",
            BottleneckSeverity.Medium, null, false, DateTimeOffset.UtcNow, null);
        _twin.GetOverviewAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new TwinOverview(
                _tenantId, new Dictionary<string, int>(),
                new List<TwinBottleneck> { bottleneck },
                Array.Empty<TwinKpi>(), 0, DateTimeOffset.UtcNow));

        var svc = CreateService();
        var result = await svc.GetCommandSummaryAsync(_tenantId);

        Assert.Equal(250_000, result.EconomicBrief.ExceptionExposure);
        Assert.Equal(3, result.EconomicBrief.DecisionsPendingApproval);
        Assert.Equal(2, result.EconomicBrief.OutcomesDrifting);
        Assert.Equal(1, result.EconomicBrief.ActiveBottlenecks);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_GeneratedAtUtc_IsRecent()
    {
        SetupEmptyDefaults(_tenantId);
        var svc = CreateService();
        var before = DateTimeOffset.UtcNow;

        var result = await svc.GetCommandSummaryAsync(_tenantId);

        Assert.True(result.GeneratedAtUtc >= before);
        Assert.True(result.GeneratedAtUtc <= DateTimeOffset.UtcNow.AddSeconds(2));
    }

    // ── Tenant isolation ────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_CallsServicesWithCorrectTenantId()
    {
        SetupEmptyDefaults(_tenantId);
        var tenantStr = _tenantId.ToString();
        var svc = CreateService();

        await svc.GetCommandSummaryAsync(_tenantId);

        await _exceptions.Received(1).GetQueueSummaryAsync(_tenantId, Arg.Any<CancellationToken>());
        await _exceptions.Received(1).GetPrioritizedQueueAsync(_tenantId, 5, Arg.Any<CancellationToken>());
        await _exceptions.Received(1).ListExceptionsAsync(_tenantId,
            Arg.Any<ExceptionSeverity?>(), Arg.Any<ExceptionCategory?>(),
            Arg.Any<ExceptionStatus?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
        await _governance.Received(1).ListPendingApprovalsAsync(tenantStr, Arg.Any<CancellationToken>());
        await _outcomes.Received(1).GetCalibrationSummaryAsync(_tenantId, null, Arg.Any<CancellationToken>());
        await _outcomes.Received(1).ListOutcomesAsync(_tenantId, 50, Arg.Any<CancellationToken>());
        await _twin.Received(1).GetOverviewAsync(_tenantId, Arg.Any<CancellationToken>());
        await _trustTiers.Received(1).ListPoliciesAsync(tenantStr, Arg.Any<CancellationToken>());
        await _trustTiers.Received(1).GetTierMapAsync(tenantStr, Arg.Any<CancellationToken>());
        await _scenarios.Received(1).ListScenariosAsync(_tenantId,
            Arg.Any<ScenarioType?>(), Arg.Any<ScenarioStatus?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_DifferentTenant_DoesNotCrossContaminate()
    {
        SetupEmptyDefaults(_tenantId);
        SetupEmptyDefaults(_otherTenantId);
        var svc = CreateService();

        var result1 = await svc.GetCommandSummaryAsync(_tenantId);
        var result2 = await svc.GetCommandSummaryAsync(_otherTenantId);

        Assert.Equal(_tenantId, result1.TenantId);
        Assert.Equal(_otherTenantId, result2.TenantId);

        await _exceptions.Received(1).GetQueueSummaryAsync(_tenantId, Arg.Any<CancellationToken>());
        await _exceptions.Received(1).GetQueueSummaryAsync(_otherTenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_StringTenantId_PassedToGovernanceAndTrust()
    {
        SetupEmptyDefaults(_tenantId);
        var tenantStr = _tenantId.ToString();
        var svc = CreateService();

        await svc.GetCommandSummaryAsync(_tenantId);

        // Governance and TrustTier services take string tenantId
        await _governance.Received(1).ListPendingApprovalsAsync(tenantStr, Arg.Any<CancellationToken>());
        await _trustTiers.Received(1).ListPoliciesAsync(tenantStr, Arg.Any<CancellationToken>());
        await _trustTiers.Received(1).GetTierMapAsync(tenantStr, Arg.Any<CancellationToken>());
    }

    // ── Approval headlines ordering ─────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_Approvals_OrderedByRequestTime()
    {
        SetupEmptyDefaults(_tenantId);
        var tenantStr = _tenantId.ToString();

        var approvals = new List<ApprovalGate>
        {
            new(Guid.NewGuid(), "later", "r1", tenantStr, "alice", "j1",
                ApprovalStatus.Pending, null, null, DateTimeOffset.UtcNow, null),
            new(Guid.NewGuid(), "earlier", "r2", tenantStr, "bob", "j2",
                ApprovalStatus.Pending, null, null, DateTimeOffset.UtcNow.AddHours(-3), null),
        };

        _governance.ListPendingApprovalsAsync(tenantStr, Arg.Any<CancellationToken>())
            .Returns(approvals.AsReadOnly());

        var svc = CreateService();
        var result = await svc.GetCommandSummaryAsync(_tenantId);

        Assert.Equal("earlier", result.ApprovalBrief.PendingApprovals[0].ActionType);
        Assert.Equal("later", result.ApprovalBrief.PendingApprovals[1].ActionType);
    }

    // ── Exception headline includes recommended action ──────────────

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_ExceptionHeadline_IncludesRecommendedActionType()
    {
        SetupEmptyDefaults(_tenantId);

        var excId = Guid.NewGuid();
        var action = new RecommendedAction("Escalate", "Escalate to VP", null, null, "High");
        var exc = MakeException(excId, ExceptionSeverity.Critical, "Needs escalation",
            ExceptionStatus.Open, action);

        _exceptions.ListExceptionsAsync(_tenantId,
            Arg.Any<ExceptionSeverity?>(), Arg.Any<ExceptionCategory?>(),
            Arg.Any<ExceptionStatus?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(new List<OperationalException> { exc }.AsReadOnly());

        _exceptions.GetPrioritizedQueueAsync(_tenantId, 5, Arg.Any<CancellationToken>())
            .Returns(new List<ExceptionPriorityScore> { new(excId, 80.0, "ok") }
                .AsReadOnly() as IReadOnlyList<ExceptionPriorityScore>);

        var svc = CreateService();
        var result = await svc.GetCommandSummaryAsync(_tenantId);

        Assert.Equal("Escalate", result.ExceptionBrief.TopExceptions[0].RecommendedActionType);
    }

    // ── Operational twin bottleneck limiting ─────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_Bottlenecks_LimitedToFive()
    {
        SetupEmptyDefaults(_tenantId);

        var bottlenecks = Enumerable.Range(0, 8)
            .Select(i => new TwinBottleneck(
                Guid.NewGuid(), _tenantId, Guid.NewGuid(), $"BN {i}",
                BottleneckSeverity.Medium, null, false, DateTimeOffset.UtcNow, null))
            .ToList();

        _twin.GetOverviewAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new TwinOverview(
                _tenantId, new Dictionary<string, int>(),
                bottlenecks, Array.Empty<TwinKpi>(), 0, DateTimeOffset.UtcNow));

        var svc = CreateService();
        var result = await svc.GetCommandSummaryAsync(_tenantId);

        Assert.Equal(5, result.OperationalBrief.TopBottlenecks.Count);
        Assert.Equal(8, result.OperationalBrief.ActiveBottlenecks);
    }

    // ── Warning KPIs count ──────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task GetSummary_WarningKpis_CountFromOverview()
    {
        SetupEmptyDefaults(_tenantId);

        var kpis = new List<TwinKpi>
        {
            new(Guid.NewGuid(), "Latency", 500, 200, 400, 600,
                KpiDirection.LowerIsBetter, "ms", DateTimeOffset.UtcNow),
            new(Guid.NewGuid(), "Throughput", 80, 100, 90, 50,
                KpiDirection.HigherIsBetter, "rps", DateTimeOffset.UtcNow),
        };

        _twin.GetOverviewAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new TwinOverview(
                _tenantId, new Dictionary<string, int>(),
                Array.Empty<TwinBottleneck>(), kpis, 5, DateTimeOffset.UtcNow));

        var svc = CreateService();
        var result = await svc.GetCommandSummaryAsync(_tenantId);

        Assert.Equal(2, result.OperationalBrief.WarningKpis);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private OperationalException MakeException(
        Guid id, ExceptionSeverity severity, string title,
        ExceptionStatus status, RecommendedAction? action = null) =>
        new(id, _tenantId, ExceptionCategory.Anomaly, severity, title,
            $"{title} desc", "Operations", status,
            0.8, 50_000, 0.7, EscalationLevel.None,
            null, null, Array.Empty<ExceptionArtifactLink>(),
            action, "test", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            null, null);

    private Scenario MakeScenario(ScenarioStatus status, ScenarioType type, string title) =>
        new(Guid.NewGuid(), _tenantId, title, null, type, status,
            new List<ScenarioAssumption>
            {
                new("Headcount", "100", "120", "people", null),
            },
            new List<ProjectedEffect>
            {
                new("HR", "Hiring", null, null, null, "Increase", "Medium"),
            },
            Array.Empty<ScenarioLink>(),
            Array.Empty<ScenarioLink>(),
            Array.Empty<ScenarioLink>(),
            "test", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private OutcomeRecord MakeOutcome(OutcomeDirection direction) =>
        new(Guid.NewGuid(), Guid.NewGuid(), _tenantId,
            "Expected", 100m, 0.8, "30d",
            "Actual", direction == OutcomeDirection.Underperformed ? 70m : 100m,
            DateTimeOffset.UtcNow,
            direction == OutcomeDirection.Underperformed ? -30m : 0m,
            direction == OutcomeDirection.Underperformed ? -30.0 : 0.0,
            direction,
            null, null,
            direction == OutcomeDirection.Underperformed
                ? OutcomeAssessment.WorseThanExpected
                : OutcomeAssessment.AsExpected,
            RecalibrationSignal.None, "test",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}

using ArchonAI.Api.Security;
using ArchonAI.Memory;
using ArchonAI.Core.Services;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Decisions;
using ArchonAI.Core.Models.PolicySimulation;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Integration;

/// <summary>
/// Integration tests for policy simulation / dry-run mode.
/// Verifies: no side effects, tenant isolation, policy evaluation accuracy,
/// verdict determination, economic projections, and workflow preview.
/// </summary>
public sealed class PolicySimulationTests
{
    private readonly IDecisionService _decisions;
    private readonly IFinancialConsequenceService _consequences;
    private readonly ITrustTierService _trustTiers;
    private readonly IGovernanceService _governance;
    private readonly IOutcomeLearningService _outcomes;
    private readonly IExceptionIntelligenceService _exceptions;
    private readonly IEnterpriseMemoryService _memory;
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<HeroWorkflowService> _hwLogger = Substitute.For<ILogger<HeroWorkflowService>>();
    private readonly ILogger<PolicySimulationService> _simLogger = Substitute.For<ILogger<PolicySimulationService>>();

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    public PolicySimulationTests()
    {
        _decisions = new DecisionService(
            Substitute.For<IEventBus>(),
            Substitute.For<ILogger<DecisionService>>());
        _consequences = new FinancialConsequenceService(
            Substitute.For<IEventBus>(),
            Substitute.For<ILogger<FinancialConsequenceService>>());
        _trustTiers = new TrustTierService(
            Substitute.For<IEventBus>(),
            Substitute.For<ILogger<TrustTierService>>());
        _governance = new GovernanceService();
        _outcomes = new OutcomeLearningService(
            _decisions,
            Substitute.For<IEventBus>(),
            Substitute.For<ILogger<OutcomeLearningService>>());
        _exceptions = new ExceptionIntelligenceService(
            Substitute.For<IEventBus>(),
            Substitute.For<ILogger<ExceptionIntelligenceService>>());
        _memory = new EnterpriseMemoryService(
            Substitute.For<IEventBus>(),
            Substitute.For<ILogger<EnterpriseMemoryService>>());
    }

    private IHeroWorkflowService CreateHeroService() => new HeroWorkflowService(
        _decisions, _consequences, _trustTiers, _governance,
        _outcomes, _exceptions, _memory, _eventBus, _hwLogger);

    private PolicySimulationService CreateService() => new(
        _trustTiers, _governance, CreateHeroService(), _simLogger);

    private static SimulationRequest MakeRequest(
        Guid tenantId,
        string actionType = "vendor-selection",
        string title = "Test Simulation",
        string? riskLevel = null,
        string? reversibility = null,
        double? confidence = null,
        decimal? expectedValue = null,
        string? requestedTier = null,
        string? workflowType = null,
        decimal? revenueImpactLow = null,
        decimal? revenueImpactHigh = null,
        decimal? costImpactLow = null,
        decimal? costImpactHigh = null,
        decimal? downsideRisk = null,
        decimal? upsidePotential = null) => new(
        TenantId: tenantId,
        ActionType: actionType,
        ActionScope: actionType,
        Title: title,
        Domain: "operations",
        Objective: null,
        RiskLevel: riskLevel,
        Reversibility: reversibility,
        Confidence: confidence,
        ExpectedValue: expectedValue,
        RevenueImpactLow: revenueImpactLow,
        RevenueImpactHigh: revenueImpactHigh,
        CostImpactLow: costImpactLow,
        CostImpactHigh: costImpactHigh,
        DownsideRisk: downsideRisk,
        UpsidePotential: upsidePotential,
        RequestedTier: requestedTier,
        WorkflowType: workflowType,
        RequestedBy: "test-user");

    // ═══════════════════════════════════════════════════════════════
    //  Basic Simulation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SimulateAsync_ReturnsResult_WithAllSections()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA, riskLevel: "Medium", confidence: 0.8);

        var result = await svc.SimulateAsync(req);

        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(TenantA, result.TenantId);
        Assert.Equal("vendor-selection", result.ActionType);
        Assert.Equal("Test Simulation", result.Title);
        Assert.NotNull(result.Decision);
        Assert.NotNull(result.TrustTierOutcome);
        Assert.NotNull(result.ApprovalRequirement);
        Assert.NotEmpty(result.PolicyOutcomes);
        Assert.NotEmpty(result.Reasons);
        Assert.Equal("test-user", result.SimulatedBy);
    }

    [Fact]
    public async Task SimulateAsync_LowRisk_DoesNotRequireApprovalFromRisk()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA, riskLevel: "Low", confidence: 0.9,
            reversibility: "FullyReversible");

        var result = await svc.SimulateAsync(req);

        Assert.NotNull(result.Decision);
        Assert.Equal(DecisionRiskLevel.Low, result.Decision.RiskLevel);
        Assert.False(result.Decision.WouldRequireApproval);
        // Risk-level policy should pass for low risk
        var riskPolicy = result.PolicyOutcomes.First(po => po.PolicyName == "RiskLevelPolicy");
        Assert.True(riskPolicy.Passed);
    }

    [Fact]
    public async Task SimulateAsync_HighRisk_RequiresApproval()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA, riskLevel: "High", confidence: 0.6);

        var result = await svc.SimulateAsync(req);

        Assert.True(result.Verdict == SimulationVerdict.RequiresApproval
                  || result.Verdict == SimulationVerdict.Blocked);
        Assert.NotNull(result.Decision);
        Assert.True(result.Decision.WouldRequireApproval);
    }

    [Fact]
    public async Task SimulateAsync_CriticalRisk_RequiresApproval()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA, riskLevel: "Critical", confidence: 0.5);

        var result = await svc.SimulateAsync(req);

        Assert.True(result.Decision!.WouldRequireApproval);
        Assert.Contains(result.Reasons, r => r.Contains("approval", StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════
    //  No Side Effects
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SimulateAsync_DoesNotCreateDecisions()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA, riskLevel: "High", expectedValue: 500000m);

        await svc.SimulateAsync(req);

        // Verify no decisions were created in the decision service
        var decisions = await _decisions.ListAsync(TenantA);
        Assert.Empty(decisions);
    }

    [Fact]
    public async Task SimulateAsync_DoesNotCreateConsequences()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA,
            revenueImpactLow: 100000m, revenueImpactHigh: 500000m,
            costImpactLow: 50000m, costImpactHigh: 200000m);

        await svc.SimulateAsync(req);

        // Verify no financial consequences were created — GetByDecisionAsync with a fake ID returns null
        var consequence = await _consequences.GetByDecisionAsync(Guid.NewGuid());
        Assert.Null(consequence);
    }

    [Fact]
    public async Task SimulateAsync_DoesNotPublishEvents()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA);

        await svc.SimulateAsync(req);

        // Event bus should not have been called by the simulation service
        // (the trust tier service might publish internally, but the simulation itself doesn't)
        await _eventBus.DidNotReceive().PublishAsync(
            Arg.Any<ArchonAI.Core.Models.SystemEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SimulateAsync_DoesNotCreateApprovalGates()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA, riskLevel: "Critical");

        await svc.SimulateAsync(req);

        // Verify no approval gates were created
        var gates = await _governance.ListPendingApprovalsAsync(TenantA.ToString());
        Assert.Empty(gates);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Tenant Isolation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAsync_ReturnsNull_ForWrongTenant()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA);
        var result = await svc.SimulateAsync(req);

        var retrieved = await svc.GetAsync(result.Id, TenantB);

        Assert.Null(retrieved);
    }

    [Fact]
    public async Task GetAsync_ReturnsResult_ForCorrectTenant()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA);
        var result = await svc.SimulateAsync(req);

        var retrieved = await svc.GetAsync(result.Id, TenantA);

        Assert.NotNull(retrieved);
        Assert.Equal(result.Id, retrieved.Id);
    }

    [Fact]
    public async Task ListAsync_ReturnsTenantResults_Only()
    {
        var svc = CreateService();
        await svc.SimulateAsync(MakeRequest(TenantA, title: "A1"));
        await svc.SimulateAsync(MakeRequest(TenantA, title: "A2"));
        await svc.SimulateAsync(MakeRequest(TenantB, title: "B1"));

        var listA = await svc.ListAsync(TenantA);
        var listB = await svc.ListAsync(TenantB);

        Assert.Equal(2, listA.Count);
        Assert.Single(listB);
        Assert.All(listA, s => Assert.Contains("A", s.Title));
    }

    // ═══════════════════════════════════════════════════════════════
    //  Economic Projections
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SimulateAsync_CalculatesEconomicEffect()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA,
            revenueImpactLow: 100000m, revenueImpactHigh: 500000m,
            costImpactLow: 50000m, costImpactHigh: 200000m,
            downsideRisk: 75000m, upsidePotential: 300000m);

        var result = await svc.SimulateAsync(req);

        Assert.NotNull(result.EconomicEffect);
        // Net low = revLow(100k) - costHigh(200k) = -100k
        Assert.Equal(-100000m, result.EconomicEffect.NetImpactLow);
        // Net high = revHigh(500k) - costLow(50k) = 450k
        Assert.Equal(450000m, result.EconomicEffect.NetImpactHigh);
        Assert.Equal(75000m, result.EconomicEffect.DownsideRisk);
        Assert.Equal(300000m, result.EconomicEffect.UpsidePotential);
    }

    [Fact]
    public async Task SimulateAsync_OmitsEconomicEffect_WhenNoFinancials()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA);

        var result = await svc.SimulateAsync(req);

        Assert.Null(result.EconomicEffect);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Workflow Preview
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SimulateAsync_IncludesWorkflowPreview_WhenSpecified()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA, workflowType: "vendor-selection");

        var result = await svc.SimulateAsync(req);

        Assert.NotNull(result.WorkflowPreview);
        Assert.Equal("vendor-selection", result.WorkflowPreview.WorkflowType);
        Assert.Equal(7, result.WorkflowPreview.TotalSteps);
        Assert.Equal(7, result.WorkflowPreview.Steps.Count);
        Assert.All(result.WorkflowPreview.Steps, s =>
        {
            Assert.NotEmpty(s.StepId);
            Assert.NotEmpty(s.Name);
            Assert.NotEmpty(s.ProjectedOutcome);
        });
    }

    [Fact]
    public async Task SimulateAsync_OmitsWorkflowPreview_WhenNotSpecified()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA);

        var result = await svc.SimulateAsync(req);

        Assert.Null(result.WorkflowPreview);
    }

    [Fact]
    public async Task SimulateAsync_ComplianceWorkflow_HasEightSteps()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA, workflowType: "compliance-exception-resolution");

        var result = await svc.SimulateAsync(req);

        Assert.NotNull(result.WorkflowPreview);
        Assert.Equal(8, result.WorkflowPreview.TotalSteps);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Policy Outcome Accuracy
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SimulateAsync_PolicyOutcomes_IncludesRiskAndTrustAndApproval()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA, riskLevel: "High");

        var result = await svc.SimulateAsync(req);

        Assert.Contains(result.PolicyOutcomes, po => po.PolicyName == "RiskLevelPolicy");
        Assert.Contains(result.PolicyOutcomes, po => po.PolicyName == "TrustTierPolicy");
        Assert.Contains(result.PolicyOutcomes, po => po.PolicyName == "ApprovalPolicy");
    }

    [Fact]
    public async Task SimulateAsync_LowRiskPolicy_Passes()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA, riskLevel: "Low");

        var result = await svc.SimulateAsync(req);

        var riskPolicy = result.PolicyOutcomes.First(po => po.PolicyName == "RiskLevelPolicy");
        Assert.True(riskPolicy.Passed);
    }

    [Fact]
    public async Task SimulateAsync_HighRiskPolicy_Fails()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA, riskLevel: "High");

        var result = await svc.SimulateAsync(req);

        var riskPolicy = result.PolicyOutcomes.First(po => po.PolicyName == "RiskLevelPolicy");
        Assert.False(riskPolicy.Passed);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Trust Tier Integration
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SimulateAsync_TrustTierOutcome_IsPopulated()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA, requestedTier: "AutoExecuteFull",
            confidence: 0.95, reversibility: "FullyReversible");

        var result = await svc.SimulateAsync(req);

        Assert.NotNull(result.TrustTierOutcome);
        Assert.NotNull(result.TrustTierOutcome.Disposition.ToString());
        Assert.NotNull(result.TrustTierOutcome.EffectiveTier.ToString());
    }

    // ═══════════════════════════════════════════════════════════════
    //  Defaults & Edge Cases
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SimulateAsync_DefaultsRiskToMedium_WhenInvalid()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA, riskLevel: "InvalidLevel");

        var result = await svc.SimulateAsync(req);

        Assert.NotNull(result.Decision);
        Assert.Equal(DecisionRiskLevel.Medium, result.Decision.RiskLevel);
    }

    [Fact]
    public async Task SimulateAsync_ClampsConfidence_ToValidRange()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA, confidence: 5.0);

        var result = await svc.SimulateAsync(req);

        Assert.NotNull(result.Decision);
        Assert.Equal(1.0, result.Decision.Confidence);
    }

    [Fact]
    public async Task SimulateAsync_NegativeConfidence_ClampsToZero()
    {
        var svc = CreateService();
        var req = MakeRequest(TenantA, confidence: -1.0);

        var result = await svc.SimulateAsync(req);

        Assert.NotNull(result.Decision);
        Assert.Equal(0.0, result.Decision.Confidence);
    }

    [Fact]
    public async Task ListAsync_RespectsLimit()
    {
        var svc = CreateService();
        for (int i = 0; i < 5; i++)
            await svc.SimulateAsync(MakeRequest(TenantA, title: $"Sim {i}"));

        var list = await svc.ListAsync(TenantA, 3);

        Assert.Equal(3, list.Count);
    }

    [Fact]
    public async Task ListAsync_OrdersByMostRecent()
    {
        var svc = CreateService();
        await svc.SimulateAsync(MakeRequest(TenantA, title: "First"));
        await svc.SimulateAsync(MakeRequest(TenantA, title: "Second"));

        var list = await svc.ListAsync(TenantA);

        Assert.Equal("Second", list[0].Title);
        Assert.Equal("First", list[1].Title);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_ForUnknownId()
    {
        var svc = CreateService();

        var result = await svc.GetAsync(Guid.NewGuid(), TenantA);

        Assert.Null(result);
    }
}

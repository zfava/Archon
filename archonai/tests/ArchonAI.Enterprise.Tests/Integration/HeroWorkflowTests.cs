using ArchonAI.Api.Security;
using ArchonAI.Memory;
using ArchonAI.Core.Services;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.HeroWorkflow;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Integration;

/// <summary>
/// Integration tests for the hero workflow service: catalog, lifecycle progression,
/// tenant isolation, approval/execution linkage, and artifact creation.
/// </summary>
public sealed class HeroWorkflowTests
{
    private readonly IDecisionService _decisions;
    private readonly IFinancialConsequenceService _consequences;
    private readonly ITrustTierService _trustTiers;
    private readonly IGovernanceService _governance;
    private readonly IOutcomeLearningService _outcomes;
    private readonly IExceptionIntelligenceService _exceptions;
    private readonly IEnterpriseMemoryService _memory;
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<HeroWorkflowService> _logger = Substitute.For<ILogger<HeroWorkflowService>>();

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    public HeroWorkflowTests()
    {
        // Use real in-memory service implementations for integration testing
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

    private HeroWorkflowService CreateService() => new(
        _decisions, _consequences, _trustTiers, _governance,
        _outcomes, _exceptions, _memory, _eventBus, _logger);

    // ═══════════════════════════════════════════════════════════════
    //  Catalog Tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetCatalog_ReturnsThreeWorkflows()
    {
        var svc = CreateService();
        var catalog = await svc.GetCatalogAsync();

        Assert.Equal(3, catalog.Count);
        Assert.Contains(catalog, d => d.WorkflowType == "vendor-selection");
        Assert.Contains(catalog, d => d.WorkflowType == "revenue-forecast-override");
        Assert.Contains(catalog, d => d.WorkflowType == "compliance-exception-resolution");
    }

    [Fact]
    public async Task GetDefinition_ReturnsCorrectWorkflow()
    {
        var svc = CreateService();
        var def = await svc.GetDefinitionAsync("vendor-selection");

        Assert.NotNull(def);
        Assert.Equal("Vendor Selection", def.DisplayName);
        Assert.Equal(7, def.Steps.Count);
        Assert.Equal("procurement", def.Domain);
    }

    [Fact]
    public async Task GetDefinition_UnknownType_ReturnsNull()
    {
        var svc = CreateService();
        var def = await svc.GetDefinitionAsync("nonexistent-workflow");
        Assert.Null(def);
    }

    [Theory]
    [InlineData("vendor-selection", HeroWorkflowCategory.Strategic)]
    [InlineData("compliance-exception-resolution", HeroWorkflowCategory.Compliance)]
    public async Task GetDefinition_CorrectCategory(string type, HeroWorkflowCategory expected)
    {
        var svc = CreateService();
        var def = await svc.GetDefinitionAsync(type);
        Assert.NotNull(def);
        Assert.Equal(expected, def.Category);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Lifecycle Progression Tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Start_CreatesInstanceWithFirstStepExecuted()
    {
        var svc = CreateService();
        var instance = await svc.StartAsync(
            TenantA, "vendor-selection", "Q1 Vendor Evaluation",
            new Dictionary<string, string>
            {
                ["decisionTitle"] = "Select cloud provider",
                ["domain"] = "procurement",
                ["riskLevel"] = "High",
            },
            "user-1");

        Assert.NotNull(instance);
        Assert.Equal(TenantA, instance.TenantId);
        Assert.Equal("vendor-selection", instance.WorkflowType);
        Assert.Equal(HeroWorkflowStatus.InProgress, instance.Status);

        // First step (decision creation) should be completed
        Assert.Equal(HeroStepStatus.Completed, instance.Steps[0].Status);
        Assert.NotNull(instance.Steps[0].Detail);

        // Decision artifact should be created
        Assert.True(instance.Artifacts.ContainsKey("decisionId"));
    }

    [Fact]
    public async Task Advance_ProgressesThroughAllSteps()
    {
        var svc = CreateService();
        var instance = await svc.StartAsync(
            TenantA, "vendor-selection", "Full Lifecycle Test",
            new Dictionary<string, string>
            {
                ["decisionTitle"] = "Full lifecycle decision",
                ["domain"] = "operations",
            },
            "user-1");

        // Advance through each remaining step
        var maxSteps = 20; // safety limit
        var advances = 0;
        while (instance.Status is HeroWorkflowStatus.InProgress or HeroWorkflowStatus.AwaitingApproval
               && advances < maxSteps)
        {
            instance = (await svc.AdvanceAsync(instance.Id, TenantA,
                new Dictionary<string, string>
                {
                    ["actualOutcome"] = "Vendor delivered on time",
                    ["actualValue"] = "50000",
                }, "user-1"))!;
            advances++;
        }

        Assert.Equal(HeroWorkflowStatus.Completed, instance.Status);
        Assert.True(instance.Steps.All(s => s.Status == HeroStepStatus.Completed));
    }

    [Fact]
    public async Task Start_EmitsEvent()
    {
        var svc = CreateService();
        await svc.StartAsync(TenantA, "vendor-selection", "Event Test",
            new Dictionary<string, string>(), "user-1");

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "hero_workflow.started"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Start_ComplianceWorkflow_CreatesExceptionAndDecision()
    {
        var svc = CreateService();
        var instance = await svc.StartAsync(
            TenantA, "compliance-exception-resolution", "GDPR Compliance Gap",
            new Dictionary<string, string>
            {
                ["exceptionTitle"] = "GDPR data retention violation",
                ["exceptionSeverity"] = "Critical",
                ["exceptionCategory"] = "PolicyViolation",
                ["domain"] = "compliance",
                ["decisionTitle"] = "Implement data retention cleanup",
            },
            "compliance-officer");

        // First step raises exception
        Assert.Equal(HeroStepStatus.Completed, instance.Steps[0].Status);
        Assert.True(instance.Artifacts.ContainsKey("exceptionId"));

        // Advance to create decision
        instance = (await svc.AdvanceAsync(instance.Id, TenantA,
            new Dictionary<string, string>
            {
                ["decisionTitle"] = "Implement data retention cleanup",
                ["riskLevel"] = "High",
            }, "compliance-officer"))!;

        Assert.True(instance.Artifacts.ContainsKey("decisionId"));
    }

    // ═══════════════════════════════════════════════════════════════
    //  Tenant Isolation Tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_WrongTenant_ReturnsNull()
    {
        var svc = CreateService();
        var instance = await svc.StartAsync(
            TenantA, "vendor-selection", "Tenant A Workflow",
            new Dictionary<string, string>(), "user-1");

        var result = await svc.GetAsync(instance.Id, TenantB);
        Assert.Null(result);
    }

    [Fact]
    public async Task List_FiltersByTenant()
    {
        var svc = CreateService();
        await svc.StartAsync(TenantA, "vendor-selection", "Tenant A WF1",
            new Dictionary<string, string>(), "user-1");
        await svc.StartAsync(TenantA, "vendor-selection", "Tenant A WF2",
            new Dictionary<string, string>(), "user-1");
        await svc.StartAsync(TenantB, "vendor-selection", "Tenant B WF1",
            new Dictionary<string, string>(), "user-1");

        var tenantAList = await svc.ListAsync(TenantA);
        var tenantBList = await svc.ListAsync(TenantB);

        Assert.Equal(2, tenantAList.Count);
        Assert.Single(tenantBList);
    }

    [Fact]
    public async Task Advance_WrongTenant_ReturnsNull()
    {
        var svc = CreateService();
        var instance = await svc.StartAsync(
            TenantA, "vendor-selection", "Cross Tenant Test",
            new Dictionary<string, string>(), "user-1");

        var result = await svc.AdvanceAsync(instance.Id, TenantB, null, "user-2");
        Assert.Null(result);
    }

    [Fact]
    public async Task Cancel_WrongTenant_ReturnsNull()
    {
        var svc = CreateService();
        var instance = await svc.StartAsync(
            TenantA, "vendor-selection", "Cancel Cross Tenant",
            new Dictionary<string, string>(), "user-1");

        var result = await svc.CancelAsync(instance.Id, TenantB, "user-2");
        Assert.Null(result);
    }

    // ═══════════════════════════════════════════════════════════════
    //  List Filtering Tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task List_FiltersByWorkflowType()
    {
        var svc = CreateService();
        await svc.StartAsync(TenantA, "vendor-selection", "VS1",
            new Dictionary<string, string>(), "user-1");
        await svc.StartAsync(TenantA, "revenue-forecast-override", "RFO1",
            new Dictionary<string, string>(), "user-1");

        var vsOnly = await svc.ListAsync(TenantA, workflowType: "vendor-selection");
        Assert.Single(vsOnly);
        Assert.Equal("vendor-selection", vsOnly[0].WorkflowType);
    }

    [Fact]
    public async Task List_SummaryHasCorrectStepCounts()
    {
        var svc = CreateService();
        var instance = await svc.StartAsync(
            TenantA, "vendor-selection", "Step Count Test",
            new Dictionary<string, string>(), "user-1");

        var list = await svc.ListAsync(TenantA);
        var summary = list.First(s => s.Id == instance.Id);

        Assert.Equal(7, summary.TotalSteps);
        Assert.Equal(1, summary.CompletedSteps); // first step auto-executes
    }

    // ═══════════════════════════════════════════════════════════════
    //  Cancel Tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Cancel_SetsStatusToCancelled()
    {
        var svc = CreateService();
        var instance = await svc.StartAsync(
            TenantA, "vendor-selection", "Cancel Test",
            new Dictionary<string, string>(), "user-1");

        var cancelled = await svc.CancelAsync(instance.Id, TenantA, "admin");
        Assert.NotNull(cancelled);
        Assert.Equal(HeroWorkflowStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task Advance_CancelledWorkflow_ReturnsWithoutProgress()
    {
        var svc = CreateService();
        var instance = await svc.StartAsync(
            TenantA, "vendor-selection", "Post-Cancel Advance",
            new Dictionary<string, string>(), "user-1");

        await svc.CancelAsync(instance.Id, TenantA, "admin");

        var result = await svc.AdvanceAsync(instance.Id, TenantA, null, "user-1");
        Assert.NotNull(result);
        Assert.Equal(HeroWorkflowStatus.Cancelled, result.Status);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Artifact Creation Tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task VendorSelection_CreatesDecisionAndConsequence()
    {
        var svc = CreateService();
        var instance = await svc.StartAsync(
            TenantA, "vendor-selection", "Artifact Test",
            new Dictionary<string, string>
            {
                ["decisionTitle"] = "Select ERP vendor",
                ["expectedValue"] = "500000",
                ["revenueImpactHigh"] = "1000000",
                ["costImpactLow"] = "200000",
            },
            "user-1");

        // Decision created in first step
        Assert.True(instance.Artifacts.ContainsKey("decisionId"));
        var decisionId = Guid.Parse(instance.Artifacts["decisionId"]);

        // Verify the decision was actually stored
        var decision = await _decisions.GetAsync(decisionId);
        Assert.NotNull(decision);
        Assert.Equal("Select ERP vendor", decision.Title);

        // Advance to consequence step
        instance = (await svc.AdvanceAsync(instance.Id, TenantA, null, "user-1"))!;
        Assert.True(instance.Artifacts.ContainsKey("consequenceId"));

        // Verify the consequence was stored
        var consequence = await _consequences.GetByDecisionAsync(decisionId);
        Assert.NotNull(consequence);
    }

    [Fact]
    public async Task FullWorkflow_CreatesAllArtifacts()
    {
        var svc = CreateService();
        var instance = await svc.StartAsync(
            TenantA, "vendor-selection", "Full Artifact Test",
            new Dictionary<string, string>
            {
                ["decisionTitle"] = "Full artifact lifecycle",
            },
            "user-1");

        // Run to completion
        while (instance.Status is HeroWorkflowStatus.InProgress or HeroWorkflowStatus.AwaitingApproval)
        {
            instance = (await svc.AdvanceAsync(instance.Id, TenantA,
                new Dictionary<string, string>
                {
                    ["actualOutcome"] = "Success",
                    ["actualValue"] = "100000",
                }, "user-1"))!;
        }

        // All key artifacts should be present
        Assert.True(instance.Artifacts.ContainsKey("decisionId"));
        Assert.True(instance.Artifacts.ContainsKey("consequenceId"));
        Assert.True(instance.Artifacts.ContainsKey("trustDisposition"));
        Assert.True(instance.Artifacts.ContainsKey("approvalGateId"));
        Assert.True(instance.Artifacts.ContainsKey("outcomeId"));
    }

    // ═══════════════════════════════════════════════════════════════
    //  Approval / Trust Tier Linkage Tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task TrustTierStep_RecordsDisposition()
    {
        var svc = CreateService();
        var instance = await svc.StartAsync(
            TenantA, "vendor-selection", "Trust Tier Test",
            new Dictionary<string, string>(), "user-1");

        // Advance through consequence and trust tier steps
        instance = (await svc.AdvanceAsync(instance.Id, TenantA, null, "user-1"))!; // consequence
        instance = (await svc.AdvanceAsync(instance.Id, TenantA, null, "user-1"))!; // trust tier

        Assert.True(instance.Artifacts.ContainsKey("trustDisposition"));
        Assert.True(instance.Artifacts.ContainsKey("effectiveTier"));
    }

    [Fact]
    public async Task ApprovalStep_CreatesGovernanceGate()
    {
        var svc = CreateService();
        var instance = await svc.StartAsync(
            TenantA, "vendor-selection", "Approval Test",
            new Dictionary<string, string>(), "user-1");

        // Advance to approval step (step 4: index 3)
        instance = (await svc.AdvanceAsync(instance.Id, TenantA, null, "user-1"))!; // consequence
        instance = (await svc.AdvanceAsync(instance.Id, TenantA, null, "user-1"))!; // trust tier
        instance = (await svc.AdvanceAsync(instance.Id, TenantA, null, "user-1"))!; // approval

        Assert.True(instance.Artifacts.ContainsKey("approvalGateId"));
        Assert.True(instance.Artifacts.ContainsKey("approvalStatus"));
    }

    // ═══════════════════════════════════════════════════════════════
    //  Get Non-existent workflow
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_NonExistent_ReturnsNull()
    {
        var svc = CreateService();
        var result = await svc.GetAsync(Guid.NewGuid(), TenantA);
        Assert.Null(result);
    }

    [Fact]
    public async Task Start_InvalidWorkflowType_Throws()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.StartAsync(TenantA, "invalid-type", "Bad Type",
                new Dictionary<string, string>(), "user-1"));
    }
}

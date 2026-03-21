using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Decisions;
using ArchonAI.Core.Models.HeroWorkflow;
using ArchonAI.Core.Models.Inspection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Security;

/// <summary>
/// Security tests for operator inspection tooling:
/// tenant isolation, permission-aware visibility, and retrieval integrity.
/// </summary>
public sealed class InspectionTenantIsolationTests
{
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();

    private (InspectionService Svc, IDecisionService Decisions) CreateServices()
    {
        var decisions = new DecisionService(_eventBus, NullLogger<DecisionService>.Instance);
        var heroWorkflows = Substitute.For<IHeroWorkflowService>();
        var exceptions = Substitute.For<IExceptionIntelligenceService>();

        heroWorkflows.ListAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<HeroWorkflowStatus?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<HeroWorkflowSummary>>(Array.Empty<HeroWorkflowSummary>()));

        var svc = new InspectionService(
            decisions, heroWorkflows, exceptions,
            NullLogger<InspectionService>.Instance);

        return (svc, decisions);
    }

    // ── Tenant Isolation: Decision Rationale ──────────────────────────

    [Fact]
    public async Task InspectDecision_CrossTenant_ReturnsNull()
    {
        var (svc, decisions) = CreateServices();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var decision = new DecisionRecord(
            Id: Guid.NewGuid(), TenantId: tenantA,
            Title: "Test Decision", Domain: "ops", Objective: "test",
            Constraints: Array.Empty<string>(), Assumptions: new[] { "assumption-1" },
            Alternatives: new[] {
                new DecisionAlternative("opt-1", "Option 1", "Rationale 1",
                    Array.Empty<string>(), Array.Empty<string>(), 0.8, 1000m)
            },
            RecommendedOptionId: "opt-1", Confidence: 0.85,
            Reversibility: DecisionReversibility.FullyReversible,
            RiskLevel: DecisionRiskLevel.Medium, ExpectedValue: 1000m,
            RequiresApproval: false, LinkedArtifacts: Array.Empty<DecisionLink>(),
            Status: DecisionStatus.Approved, CreatedBy: "admin",
            CreatedAtUtc: DateTimeOffset.UtcNow, UpdatedAtUtc: DateTimeOffset.UtcNow);

        await decisions.CreateAsync(decision);

        // Same tenant: should return bundle
        var bundleA = await svc.InspectDecisionRationaleAsync(decision.Id, tenantA);
        Assert.NotNull(bundleA);
        Assert.Equal(tenantA, bundleA.TenantId);

        // Cross tenant: should return null
        var bundleB = await svc.InspectDecisionRationaleAsync(decision.Id, tenantB);
        Assert.Null(bundleB);
    }

    // ── Tenant Isolation: Policy Evaluation ────────────────────────────

    [Fact]
    public async Task InspectPolicy_CrossTenant_ReturnsNull()
    {
        var (svc, _) = CreateServices();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var eval = new PolicyEvaluationResult(
            EvaluationId: Guid.NewGuid(), TenantId: tenantA,
            SubjectType: "action", SubjectId: "act-1",
            IsAllowed: true, RiskScore: 10, ConfidenceScore: 0.9,
            RequiresApproval: false, ApprovalState: "not-required",
            ManualOverrideState: "none", ApprovalCheckpoint: "none",
            GuardrailViolations: Array.Empty<string>(),
            RulesEvaluated: Array.Empty<PolicyRuleResult>(),
            Reason: "All checks passed.", EvaluatedAtUtc: DateTimeOffset.UtcNow);

        await svc.RecordPolicyEvaluationAsync("action", "act-1", eval);

        // Same tenant
        var resultA = await svc.InspectPolicyEvaluationAsync("action", "act-1", tenantA);
        Assert.NotNull(resultA);

        // Cross tenant
        var resultB = await svc.InspectPolicyEvaluationAsync("action", "act-1", tenantB);
        Assert.Null(resultB);
    }

    // ── Tenant Isolation: Workflow Diagnostics ─────────────────────────

    [Fact]
    public async Task InspectWorkflow_CrossTenant_ReturnsNull()
    {
        var (svc, _) = CreateServices();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var workflowId = Guid.NewGuid();

        var diag = new WorkflowFailureDiagnostics(
            WorkflowId: workflowId, TenantId: tenantA,
            WorkflowName: "Test Workflow", CurrentState: "Failed",
            FailureCategory: "step-failure", FailureReason: "Agent timeout",
            FailedStepName: "Step 2", FailedStepIndex: 1,
            StepDiagnostics: Array.Empty<WorkflowStepDiagnostic>(),
            PolicyEvaluations: Array.Empty<PolicyEvaluationResult>(),
            ContextUsed: Array.Empty<MemoryContextReference>(),
            IsRetryable: true, SuggestedRemediation: "Retry the workflow",
            RelatedExceptions: Array.Empty<LinkedArtifactReference>(),
            FailedAtUtc: DateTimeOffset.UtcNow, InspectedAtUtc: DateTimeOffset.UtcNow);

        await svc.RecordWorkflowDiagnosticsAsync(diag);

        // Same tenant
        var diagA = await svc.InspectWorkflowFailureAsync(workflowId, tenantA);
        Assert.NotNull(diagA);

        // Cross tenant
        var diagB = await svc.InspectWorkflowFailureAsync(workflowId, tenantB);
        Assert.Null(diagB);
    }

    // ── Retrieval Integrity ───────────────────────────────────────────

    [Fact]
    public async Task DecisionRationale_ContainsAllAssumptionsAndAlternatives()
    {
        var (svc, decisions) = CreateServices();
        var tenantId = Guid.NewGuid();

        var decision = new DecisionRecord(
            Id: Guid.NewGuid(), TenantId: tenantId,
            Title: "Strategic Decision", Domain: "finance", Objective: "reduce cost",
            Constraints: new[] { "budget-cap", "headcount-freeze" },
            Assumptions: new[] { "market-stable", "no-regulatory-changes", "vendor-available" },
            Alternatives: new[] {
                new DecisionAlternative("a", "Option A", "Cheapest",
                    new[] { "low cost" }, new[] { "slower" }, 0.7, 500m),
                new DecisionAlternative("b", "Option B", "Fastest",
                    new[] { "fast" }, new[] { "expensive" }, 0.9, 2000m),
            },
            RecommendedOptionId: "b", Confidence: 0.88,
            Reversibility: DecisionReversibility.PartiallyReversible,
            RiskLevel: DecisionRiskLevel.High, ExpectedValue: 2000m,
            RequiresApproval: true, LinkedArtifacts: Array.Empty<DecisionLink>(),
            Status: DecisionStatus.Proposed, CreatedBy: "cfo",
            CreatedAtUtc: DateTimeOffset.UtcNow, UpdatedAtUtc: DateTimeOffset.UtcNow);

        await decisions.CreateAsync(decision);

        var bundle = await svc.InspectDecisionRationaleAsync(decision.Id, tenantId);

        Assert.NotNull(bundle);
        Assert.Equal(3, bundle.Assumptions.Count);
        Assert.Equal(2, bundle.Constraints.Count);
        Assert.Equal(2, bundle.Alternatives.Count);
        Assert.Single(bundle.Alternatives, a => a.IsRecommended);
        Assert.Equal("b", bundle.Alternatives.First(a => a.IsRecommended).Id);
        Assert.Equal("Fastest", bundle.RecommendationRationale);
        Assert.Equal("finance", bundle.Domain);
    }

    [Fact]
    public async Task MemoryReferences_ReturnCorrectData()
    {
        var (svc, _) = CreateServices();
        var tenantId = Guid.NewGuid();

        var ref1 = new MemoryContextReference(
            Guid.NewGuid(), "Strategy", "org-memory", "Historical cost data",
            0.92, "Used for cost estimation", DateTimeOffset.UtcNow);

        var ref2 = new MemoryContextReference(
            Guid.NewGuid(), "Pattern", "knowledge-graph", "Seasonal demand pattern",
            0.87, "Demand forecasting", DateTimeOffset.UtcNow);

        await svc.RecordMemoryReferenceAsync(tenantId, "decision", "dec-1", ref1);
        await svc.RecordMemoryReferenceAsync(tenantId, "decision", "dec-1", ref2);

        var refs = await svc.InspectMemoryReferencesAsync("decision", "dec-1", tenantId);

        Assert.Equal(2, refs.Count);
        Assert.Contains(refs, r => r.MemoryType == "Strategy");
        Assert.Contains(refs, r => r.MemoryType == "Pattern");
    }

    // ── Permission-Aware Visibility: Summaries ───────────────────────

    [Fact]
    public async Task ListSummaries_OnlyReturnsTenantData()
    {
        var (svc, decisions) = CreateServices();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await decisions.CreateAsync(new DecisionRecord(
            Guid.NewGuid(), tenantA, "Decision A", "ops", "", Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<DecisionAlternative>(), "", 0.5,
            DecisionReversibility.FullyReversible, DecisionRiskLevel.Low, null, false,
            Array.Empty<DecisionLink>(), DecisionStatus.Draft, "admin",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        await decisions.CreateAsync(new DecisionRecord(
            Guid.NewGuid(), tenantB, "Decision B", "ops", "", Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<DecisionAlternative>(), "", 0.5,
            DecisionReversibility.FullyReversible, DecisionRiskLevel.Low, null, false,
            Array.Empty<DecisionLink>(), DecisionStatus.Draft, "admin",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var summariesA = await svc.ListInspectionSummariesAsync(tenantA);
        var summariesB = await svc.ListInspectionSummariesAsync(tenantB);

        Assert.Single(summariesA);
        Assert.Equal("Decision A", summariesA[0].Title);
        Assert.Single(summariesB);
        Assert.Equal("Decision B", summariesB[0].Title);
    }

    // ── Nonexistent subjects ──────────────────────────────────────────

    [Fact]
    public async Task InspectNonexistentDecision_ReturnsNull()
    {
        var (svc, _) = CreateServices();
        var result = await svc.InspectDecisionRationaleAsync(Guid.NewGuid(), Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task InspectNonexistentWorkflow_ReturnsNull()
    {
        var (svc, _) = CreateServices();
        var result = await svc.InspectWorkflowFailureAsync(Guid.NewGuid(), Guid.NewGuid());
        Assert.Null(result);
    }
}

using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Inspection;
using ArchonAI.Core.Models.ProofAnalytics;
using ArchonAI.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Integration;

/// <summary>
/// Tests for inspection persistence via the unified IInspectionService interface.
/// Validates record/retrieve roundtrip, tenant isolation, subscriber wiring,
/// workflow diagnostics recording, and data volatility of in-memory store.
/// </summary>
public sealed class InspectionPersistenceTests
{
    private static IInspectionService CreateInMemoryInspectionService() =>
        new InspectionService(
            Substitute.For<IDecisionService>(),
            Substitute.For<IHeroWorkflowService>(),
            Substitute.For<IExceptionIntelligenceService>(),
            NullLogger<InspectionService>.Instance);

    // ── Test 1: Record and retrieve policy evaluation via interface ──

    [Fact]
    public async Task RecordAndRetrieve_PolicyEvaluation_ViaInterface()
    {
        IInspectionService svc = CreateInMemoryInspectionService();
        var tenantId = Guid.NewGuid();
        var subjectId = Guid.NewGuid().ToString();

        var eval = new PolicyEvaluationResult(
            EvaluationId: Guid.NewGuid(),
            TenantId: tenantId,
            SubjectType: "decision",
            SubjectId: subjectId,
            IsAllowed: false,
            RiskScore: 42.5,
            ConfidenceScore: 0.78,
            RequiresApproval: true,
            ApprovalState: "pending",
            ManualOverrideState: "none",
            ApprovalCheckpoint: "governance-gate",
            GuardrailViolations: new[] { "budget-exceeded" },
            RulesEvaluated: new List<PolicyRuleResult>
            {
                new("budget-check", "finance", false, 42.5, "Budget limit exceeded"),
            },
            Reason: "Budget guardrail violated.",
            EvaluatedAtUtc: DateTimeOffset.UtcNow);

        await svc.RecordPolicyEvaluationAsync("decision", subjectId, eval);

        var result = await svc.InspectPolicyEvaluationAsync("decision", subjectId, tenantId);

        Assert.NotNull(result);
        Assert.False(result.IsAllowed);
        Assert.Equal(42.5, result.RiskScore);
        Assert.Equal(0.78, result.ConfidenceScore);
        Assert.Equal("Budget guardrail violated.", result.Reason);
    }

    // ── Test 2: Record and retrieve memory reference via interface ──

    [Fact]
    public async Task RecordAndRetrieve_MemoryReference_ViaInterface()
    {
        IInspectionService svc = CreateInMemoryInspectionService();
        var tenantId = Guid.NewGuid();
        var subjectId = "mem-subject-1";

        var memRef = new MemoryContextReference(
            MemoryId: Guid.NewGuid(),
            MemoryType: "Operational",
            Source: "enterprise-memory",
            ContentSummary: "Q4 performance data",
            RelevanceScore: 0.91,
            UsageContext: "cost-analysis",
            RetrievedAtUtc: DateTimeOffset.UtcNow);

        await svc.RecordMemoryReferenceAsync(tenantId, "analysis", subjectId, memRef);

        var refs = await svc.InspectMemoryReferencesAsync("analysis", subjectId, tenantId);

        Assert.Single(refs);
        Assert.Equal("Operational", refs[0].MemoryType);
        Assert.Equal(0.91, refs[0].RelevanceScore);
        Assert.Equal("Q4 performance data", refs[0].ContentSummary);
    }

    // ── Test 3: Record multiple memory references, retrieve all ──

    [Fact]
    public async Task RecordMultipleMemoryReferences_ReturnsAll()
    {
        IInspectionService svc = CreateInMemoryInspectionService();
        var tenantId = Guid.NewGuid();
        var subjectId = "multi-ref-subject";

        for (int i = 0; i < 3; i++)
        {
            await svc.RecordMemoryReferenceAsync(tenantId, "decision", subjectId, new MemoryContextReference(
                Guid.NewGuid(), $"Type-{i}", "source", $"Summary {i}", 0.5 + i * 0.1,
                "context", DateTimeOffset.UtcNow));
        }

        var refs = await svc.InspectMemoryReferencesAsync("decision", subjectId, tenantId);

        Assert.Equal(3, refs.Count);
    }

    // ── Test 4: Record and retrieve workflow diagnostics via interface ──

    [Fact]
    public async Task RecordAndRetrieve_WorkflowDiagnostics_ViaInterface()
    {
        IInspectionService svc = CreateInMemoryInspectionService();
        var tenantId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();

        var diag = new WorkflowFailureDiagnostics(
            WorkflowId: workflowId,
            TenantId: tenantId,
            WorkflowName: "Deploy Pipeline",
            CurrentState: "Failed",
            FailureCategory: "step-failure",
            FailureReason: "Agent timeout on step 3",
            FailedStepName: "execute-deploy",
            FailedStepIndex: 2,
            StepDiagnostics: Array.Empty<WorkflowStepDiagnostic>(),
            PolicyEvaluations: Array.Empty<PolicyEvaluationResult>(),
            ContextUsed: Array.Empty<MemoryContextReference>(),
            IsRetryable: true,
            SuggestedRemediation: "Retry with extended timeout",
            RelatedExceptions: Array.Empty<LinkedArtifactReference>(),
            FailedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            InspectedAtUtc: DateTimeOffset.UtcNow);

        await svc.RecordWorkflowDiagnosticsAsync(diag);

        var result = await svc.InspectWorkflowFailureAsync(workflowId, tenantId);

        Assert.NotNull(result);
        Assert.Equal(workflowId, result.WorkflowId);
        Assert.Equal("step-failure", result.FailureCategory);
        Assert.Equal("Agent timeout on step 3", result.FailureReason);
        Assert.True(result.IsRetryable);
    }

    // ── Test 5: In-memory data lost after new instance ──

    [Fact]
    public async Task InMemoryData_LostAfterNewInstance()
    {
        IInspectionService instanceA = CreateInMemoryInspectionService();
        var tenantId = Guid.NewGuid();
        var subjectId = Guid.NewGuid().ToString();

        var eval = new PolicyEvaluationResult(
            Guid.NewGuid(), tenantId, "action", subjectId,
            true, 5.0, 0.95, false, "not-required", "none", "none",
            Array.Empty<string>(),
            new List<PolicyRuleResult> { new("r1", "cat", true, 0, "ok") },
            "Passed", DateTimeOffset.UtcNow);

        await instanceA.RecordPolicyEvaluationAsync("action", subjectId, eval);

        // Verify instance A has the data
        var fromA = await instanceA.InspectPolicyEvaluationAsync("action", subjectId, tenantId);
        Assert.NotNull(fromA);
        Assert.Equal(5.0, fromA.RiskScore);

        // Create new instance B — data should be gone
        IInspectionService instanceB = CreateInMemoryInspectionService();
        var fromB = await instanceB.InspectPolicyEvaluationAsync("action", subjectId, tenantId);

        // Instance B returns synthetic fallback (IsAllowed=true, RiskScore=0)
        Assert.NotNull(fromB);
        Assert.Equal(0, fromB.RiskScore);
    }

    // ── Test 6: Subscriber records policy evaluation via interface ──

    [Fact]
    public async Task GovernanceEventSubscriber_RecordsInspection_ViaInterface()
    {
        var mockInspection = Substitute.For<IInspectionService>();
        var proofAnalytics = Substitute.For<IProofAnalyticsService>();
        var bus = new InMemoryTestEventBus();

        var subscriber = new GovernanceEventSubscriber(
            bus, mockInspection, proofAnalytics,
            NullLogger<GovernanceEventSubscriber>.Instance);
        await subscriber.StartAsync(CancellationToken.None);

        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        await bus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "inspection.policy-evaluation-recorded",
            "PolicyEngine",
            taskId,
            new Dictionary<string, string>
            {
                ["subjectType"] = "task",
                ["subjectId"] = taskId.ToString(),
                ["tenantId"] = tenantId.ToString(),
                ["isAllowed"] = "true",
                ["riskScore"] = "20",
                ["confidenceScore"] = "0.9",
                ["reason"] = "Policy passed",
            }.AsReadOnly(),
            DateTimeOffset.UtcNow));

        await mockInspection.Received(1).RecordPolicyEvaluationAsync(
            "task", taskId.ToString(),
            Arg.Any<PolicyEvaluationResult>(),
            Arg.Any<CancellationToken>());
    }

    // ── Test 7: Subscriber records memory reference via interface ──

    [Fact]
    public async Task GovernanceEventSubscriber_RecordsMemoryReference_ViaInterface()
    {
        var mockInspection = Substitute.For<IInspectionService>();
        var proofAnalytics = Substitute.For<IProofAnalyticsService>();
        var bus = new InMemoryTestEventBus();

        var subscriber = new GovernanceEventSubscriber(
            bus, mockInspection, proofAnalytics,
            NullLogger<GovernanceEventSubscriber>.Instance);
        await subscriber.StartAsync(CancellationToken.None);

        var tenantId = Guid.NewGuid();
        var memoryId = Guid.NewGuid();

        await bus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "inspection.memory-reference-recorded",
            "EnterpriseMemoryService",
            memoryId,
            new Dictionary<string, string>
            {
                ["subjectType"] = "enterprise-memory-query",
                ["subjectId"] = tenantId.ToString(),
                ["memoryRecordId"] = memoryId.ToString(),
                ["tenantId"] = tenantId.ToString(),
                ["scope"] = "Operational",
                ["category"] = "performance",
                ["summary"] = "CPU metrics",
                ["relevanceScore"] = "0.88",
            }.AsReadOnly(),
            DateTimeOffset.UtcNow));

        await mockInspection.Received(1).RecordMemoryReferenceAsync(
            tenantId,
            "enterprise-memory-query",
            tenantId.ToString(),
            Arg.Any<MemoryContextReference>(),
            Arg.Any<CancellationToken>());
    }

    // ── Test 8: Subscriber records workflow diagnostics on failure ──

    [Fact]
    public async Task GovernanceEventSubscriber_RecordsWorkflowDiagnostics_OnFailure()
    {
        var mockInspection = Substitute.For<IInspectionService>();
        var proofAnalytics = Substitute.For<IProofAnalyticsService>();
        proofAnalytics.RecordEventAsync(Arg.Any<ProofEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ProofEvent>());
        var bus = new InMemoryTestEventBus();

        var subscriber = new GovernanceEventSubscriber(
            bus, mockInspection, proofAnalytics,
            NullLogger<GovernanceEventSubscriber>.Instance);
        await subscriber.StartAsync(CancellationToken.None);

        var workflowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await bus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "hero_workflow.failed",
            "HeroWorkflowService",
            workflowId,
            new Dictionary<string, string>
            {
                ["workflowId"] = workflowId.ToString(),
                ["tenantId"] = tenantId.ToString(),
                ["reason"] = "Step execution timeout",
                ["actor"] = "system",
                ["decisionId"] = workflowId.ToString(),
            }.AsReadOnly(),
            DateTimeOffset.UtcNow));

        // Verify BOTH proof analytics AND inspection were recorded
        await proofAnalytics.Received(1).RecordEventAsync(
            Arg.Is<ProofEvent>(e => e.ActionType == "workflow.failed"),
            Arg.Any<CancellationToken>());

        await mockInspection.Received(1).RecordWorkflowDiagnosticsAsync(
            Arg.Is<WorkflowFailureDiagnostics>(d =>
                d.WorkflowId == workflowId &&
                d.TenantId == tenantId &&
                d.FailureReason == "Step execution timeout"),
            Arg.Any<CancellationToken>());
    }

    // ── Test 9: Policy evaluation tenant isolation ──

    [Fact]
    public async Task RecordPolicyEvaluation_TenantIsolation()
    {
        IInspectionService svc = CreateInMemoryInspectionService();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var subjectId = "shared-subject";

        var evalA = new PolicyEvaluationResult(
            Guid.NewGuid(), tenantA, "action", subjectId,
            true, 10.0, 0.9, false, "not-required", "none", "none",
            Array.Empty<string>(), Array.Empty<PolicyRuleResult>(),
            "Tenant A passed", DateTimeOffset.UtcNow);

        var evalB = new PolicyEvaluationResult(
            Guid.NewGuid(), tenantB, "action", subjectId,
            false, 80.0, 0.3, true, "required", "none", "approval-gate",
            new[] { "high-risk" }, Array.Empty<PolicyRuleResult>(),
            "Tenant B blocked", DateTimeOffset.UtcNow);

        // Both record to the same subject key — last write wins for in-memory
        await svc.RecordPolicyEvaluationAsync("action", subjectId, evalA);
        await svc.RecordPolicyEvaluationAsync("action", subjectId, evalB);

        // Tenant A retrieval: should return null (tenant guard — stored eval has tenantB)
        var resultA = await svc.InspectPolicyEvaluationAsync("action", subjectId, tenantA);
        Assert.Null(resultA);

        // Tenant B retrieval: should return evalB
        var resultB = await svc.InspectPolicyEvaluationAsync("action", subjectId, tenantB);
        Assert.NotNull(resultB);
        Assert.False(resultB.IsAllowed);
        Assert.Equal(80.0, resultB.RiskScore);
    }

    // ── Test 10: RetentionSweepResult includes InspectionRowsDeleted ──

    [Fact]
    public void RetentionSweepResult_IncludesInspectionField()
    {
        var result = new RetentionSweepResult(10, 5, 3, 42, 150);

        Assert.Equal(10, result.AuditRowsDeleted);
        Assert.Equal(5, result.TraceRowsDeleted);
        Assert.Equal(3, result.TelemetryRowsDeleted);
        Assert.Equal(42, result.InspectionRowsDeleted);
        Assert.Equal(150, result.DurationMs);
    }

    // ── Test infrastructure ──

    private sealed class InMemoryTestEventBus : IEventBus
    {
        private readonly Dictionary<string, List<Func<SystemEvent, CancellationToken, Task>>> _handlers = new();

        public async Task PublishAsync(SystemEvent systemEvent, CancellationToken cancellationToken = default)
        {
            if (_handlers.TryGetValue(systemEvent.EventType, out var handlers))
            {
                foreach (var handler in handlers)
                    await handler(systemEvent, cancellationToken);
            }
        }

        public Task SubscribeAsync(string eventType, Func<SystemEvent, CancellationToken, Task> handler,
            CancellationToken cancellationToken = default)
        {
            if (!_handlers.ContainsKey(eventType))
                _handlers[eventType] = new List<Func<SystemEvent, CancellationToken, Task>>();
            _handlers[eventType].Add(handler);
            return Task.CompletedTask;
        }
    }
}

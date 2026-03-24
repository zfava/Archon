using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Inspection;
using ArchonAI.Core.Models.ProofAnalytics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Integration;

/// <summary>
/// Tests that GovernanceEventSubscriber correctly dispatches inspection and proof
/// analytics events. Verifies: event subscription, policy evaluation recording,
/// decision creation proof emission, workflow proof emission, and subscriber
/// failure isolation.
/// </summary>
public sealed class GovernanceEventSubscriberTests
{
    private readonly IEventBus _eventBus = new InMemoryTestEventBus();
    private readonly InspectionService _inspectionService;
    private readonly IProofAnalyticsService _proofAnalytics = Substitute.For<IProofAnalyticsService>();

    public GovernanceEventSubscriberTests()
    {
        _inspectionService = new InspectionService(
            Substitute.For<IDecisionService>(),
            Substitute.For<IHeroWorkflowService>(),
            Substitute.For<IExceptionIntelligenceService>(),
            NullLogger<InspectionService>.Instance);
    }

    private GovernanceEventSubscriber CreateSubscriber() =>
        new(_eventBus, _inspectionService, _proofAnalytics,
            NullLogger<GovernanceEventSubscriber>.Instance);

    [Fact]
    public async Task StartAsync_SubscribesToAllEventTypes()
    {
        var subscriber = CreateSubscriber();
        await subscriber.StartAsync(CancellationToken.None);

        var bus = (InMemoryTestEventBus)_eventBus;
        Assert.Contains("inspection.policy-evaluation-recorded", bus.Subscriptions);
        Assert.Contains("inspection.memory-reference-recorded", bus.Subscriptions);
        Assert.Contains("decision.created", bus.Subscriptions);
        Assert.Contains("decision.status-updated", bus.Subscriptions);
        Assert.Contains("hero_workflow.step-completed", bus.Subscriptions);
        Assert.Contains("hero_workflow.completed", bus.Subscriptions);
        Assert.Contains("hero_workflow.failed", bus.Subscriptions);
        Assert.Contains("gated-action.executed", bus.Subscriptions);
    }

    [Fact]
    public async Task PolicyEvaluationEvent_RecordsInInspectionService()
    {
        var subscriber = CreateSubscriber();
        await subscriber.StartAsync(CancellationToken.None);

        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        await _eventBus.PublishAsync(new SystemEvent(
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
                ["riskScore"] = "15",
                ["confidenceScore"] = "0.85",
                ["requiresApproval"] = "false",
                ["approvalState"] = "not-required",
                ["manualOverrideState"] = "none",
                ["approvalCheckpoint"] = "none",
                ["violations"] = "",
                ["reason"] = "All policy checks passed.",
            }.AsReadOnly(),
            DateTimeOffset.UtcNow));

        // Verify the evaluation was recorded
        var eval = await _inspectionService.InspectPolicyEvaluationAsync(
            "task", taskId.ToString(), tenantId);
        Assert.NotNull(eval);
        Assert.True(eval.IsAllowed);
        Assert.Equal(15, eval.RiskScore);
    }

    [Fact]
    public async Task DecisionCreatedEvent_EmitsProofEvent()
    {
        var subscriber = CreateSubscriber();
        await subscriber.StartAsync(CancellationToken.None);

        var decisionId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        _proofAnalytics.RecordEventAsync(Arg.Any<ProofEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ProofEvent>());

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "decision.created",
            "PostgresDecisionStore",
            decisionId,
            new Dictionary<string, string>
            {
                ["decisionId"] = decisionId.ToString(),
                ["tenantId"] = tenantId.ToString(),
                ["title"] = "Test Decision",
            }.AsReadOnly(),
            DateTimeOffset.UtcNow));

        await _proofAnalytics.Received(1).RecordEventAsync(
            Arg.Is<ProofEvent>(e =>
                e.DecisionId == decisionId &&
                e.TenantId == tenantId &&
                e.EventType == ProofEventType.DecisionCreated),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WorkflowCompletedEvent_EmitsProofEvent()
    {
        var subscriber = CreateSubscriber();
        await subscriber.StartAsync(CancellationToken.None);

        var workflowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var decisionId = Guid.NewGuid();

        _proofAnalytics.RecordEventAsync(Arg.Any<ProofEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ProofEvent>());

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "hero_workflow.completed",
            "HeroWorkflowService",
            workflowId,
            new Dictionary<string, string>
            {
                ["workflowId"] = workflowId.ToString(),
                ["tenantId"] = tenantId.ToString(),
                ["actor"] = "operator1",
                ["decisionId"] = decisionId.ToString(),
            }.AsReadOnly(),
            DateTimeOffset.UtcNow));

        await _proofAnalytics.Received(1).RecordEventAsync(
            Arg.Is<ProofEvent>(e =>
                e.WorkflowId == workflowId &&
                e.EventType == ProofEventType.ActualOutcomeRecorded &&
                e.IsSuccess == true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WorkflowFailedEvent_EmitsProofEventWithFailure()
    {
        var subscriber = CreateSubscriber();
        await subscriber.StartAsync(CancellationToken.None);

        var workflowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        _proofAnalytics.RecordEventAsync(Arg.Any<ProofEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ProofEvent>());

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "hero_workflow.failed",
            "HeroWorkflowService",
            workflowId,
            new Dictionary<string, string>
            {
                ["workflowId"] = workflowId.ToString(),
                ["tenantId"] = tenantId.ToString(),
                ["stepId"] = "execute-decision",
                ["actor"] = "system",
                ["reason"] = "Step execution failed",
                ["decisionId"] = workflowId.ToString(),
            }.AsReadOnly(),
            DateTimeOffset.UtcNow));

        await _proofAnalytics.Received(1).RecordEventAsync(
            Arg.Is<ProofEvent>(e =>
                e.WorkflowId == workflowId &&
                e.IsSuccess == false &&
                e.ActionType == "workflow.failed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscriberFailure_DoesNotBreakEventBus()
    {
        var subscriber = CreateSubscriber();
        await subscriber.StartAsync(CancellationToken.None);

        // Configure proof analytics to throw on record
        _proofAnalytics.RecordEventAsync(Arg.Any<ProofEvent>(), Arg.Any<CancellationToken>())
            .Returns<ProofEvent>(_ => throw new InvalidOperationException("Service unavailable"));

        // Publishing should not throw even when subscriber handler fails
        var ex = await Record.ExceptionAsync(() => _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "decision.created",
            "test",
            Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["decisionId"] = Guid.NewGuid().ToString(),
                ["tenantId"] = Guid.NewGuid().ToString(),
                ["title"] = "Test",
            }.AsReadOnly(),
            DateTimeOffset.UtcNow)));

        // The event bus should not propagate subscriber failures
        // (GovernanceEventSubscriber catches exceptions internally)
        Assert.Null(ex);
    }

    [Fact]
    public async Task PolicyEngine_PublishesInspectionEvent_AfterEvaluation()
    {
        var mockEventBus = Substitute.For<IEventBus>();
        var engine = new ArchonAI.Policy.PolicyEngine(
            Microsoft.Extensions.Options.Options.Create(new ArchonAI.Policy.PolicyOptions()),
            mockEventBus,
            NullLogger<ArchonAI.Policy.PolicyEngine>.Instance);

        var agent = new Agent(Guid.NewGuid(), "test", "1.0",
            new List<AgentCapability> { new("data-read", "data-read", "general", "1.0") },
            true, DateTimeOffset.UtcNow);
        var task = new ArchonAI.Core.Models.Task(
            Guid.NewGuid(), Guid.NewGuid(), 1, "Test", "desc", "data-read",
            new Dictionary<string, string> { ["k"] = "v" },
            DateTimeOffset.UtcNow, null, null);
        var ctx = new ArchonAI.Core.Models.ExecutionContext(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "tenant-1",
            new Dictionary<string, string>(), DateTimeOffset.UtcNow);

        await engine.EvaluateAsync(agent, task, ctx);

        // Verify event bus received the inspection event
        await mockEventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "inspection.policy-evaluation-recorded"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GatedActionExecuted_EmitsProofEvent()
    {
        var subscriber = CreateSubscriber();
        await subscriber.StartAsync(CancellationToken.None);

        var gateId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        _proofAnalytics.RecordEventAsync(Arg.Any<ProofEvent>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ProofEvent>());

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "gated-action.executed",
            "GatedActionExecutor",
            gateId,
            new Dictionary<string, string>
            {
                ["actionType"] = "workflow.cancel",
                ["tenantId"] = tenantId.ToString(),
                ["success"] = "True",
                ["detail"] = "",
                ["actor"] = "admin",
            }.AsReadOnly(),
            DateTimeOffset.UtcNow));

        await _proofAnalytics.Received(1).RecordEventAsync(
            Arg.Is<ProofEvent>(e =>
                e.TenantId == tenantId &&
                e.ActionType == "workflow.cancel" &&
                e.IsSuccess == true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MemoryReferenceEvent_RecordsInInspectionService()
    {
        var subscriber = CreateSubscriber();
        await subscriber.StartAsync(CancellationToken.None);

        var tenantId = Guid.NewGuid();
        var memoryRecordId = Guid.NewGuid();

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "inspection.memory-reference-recorded",
            "EnterpriseMemoryService",
            memoryRecordId,
            new Dictionary<string, string>
            {
                ["subjectType"] = "enterprise-memory-query",
                ["subjectId"] = tenantId.ToString(),
                ["memoryRecordId"] = memoryRecordId.ToString(),
                ["tenantId"] = tenantId.ToString(),
                ["scope"] = "Operational",
                ["category"] = "performance",
                ["summary"] = "Recent performance metrics for Q4",
                ["relevanceScore"] = "0.95",
            }.AsReadOnly(),
            DateTimeOffset.UtcNow));

        // Verify the memory reference was recorded in the inspection service
        var refs = await _inspectionService.InspectMemoryReferencesAsync(
            "enterprise-memory-query", tenantId.ToString(), tenantId);
        Assert.NotNull(refs);
        Assert.Single(refs);
        Assert.Equal(memoryRecordId, refs[0].MemoryId);
        Assert.Equal("Operational", refs[0].MemoryType);
        Assert.Equal(0.95, refs[0].RelevanceScore);
    }

    [Fact]
    public async Task EnterpriseMemoryQuery_PublishesMemoryReferenceEvent()
    {
        var mockEventBus = Substitute.For<IEventBus>();
        var svc = new EnterpriseMemoryService(
            mockEventBus,
            NullLogger<EnterpriseMemoryService>.Instance);

        var tenantId = Guid.NewGuid();

        // Store a record so QueryAsync returns results
        await svc.StoreAsync(new ArchonAI.Core.Models.Memory.EnterpriseMemoryRecord(
            Id: Guid.NewGuid(),
            TenantId: tenantId,
            Layer: ArchonAI.Core.Models.Memory.MemoryLayer.Operational,
            Category: "perf",
            Subject: "CPU metrics",
            Content: "CPU at 80%",
            Metadata: new Dictionary<string, string>().AsReadOnly(),
            LinkedEntities: Array.Empty<ArchonAI.Core.Models.Memory.MemoryEntityLink>(),
            Tags: new[] { "infra" },
            Importance: 0.8,
            CreatedBy: "test",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: null));

        // Reset call tracking after StoreAsync (which also publishes)
        mockEventBus.ClearReceivedCalls();

        await svc.QueryAsync(tenantId);

        // Verify inspection event was published for the retrieved record
        await mockEventBus.Received().PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "inspection.memory-reference-recorded"
                && e.Source == "EnterpriseMemoryService"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MemoryReferenceSubscriberFailure_DoesNotBreakEventBus()
    {
        // Use a custom inspection service that throws on RecordMemoryReference
        var throwingInspection = new InspectionService(
            Substitute.For<IDecisionService>(),
            Substitute.For<IHeroWorkflowService>(),
            Substitute.For<IExceptionIntelligenceService>(),
            NullLogger<InspectionService>.Instance);

        var bus = new InMemoryTestEventBus();
        var subscriber = new GovernanceEventSubscriber(
            bus, throwingInspection, _proofAnalytics,
            NullLogger<GovernanceEventSubscriber>.Instance);
        await subscriber.StartAsync(CancellationToken.None);

        // Publish an event with invalid data — the handler should catch and not throw
        var ex = await Record.ExceptionAsync(() => bus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "inspection.memory-reference-recorded",
            "test",
            Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["subjectType"] = "test",
                ["subjectId"] = "test",
                ["memoryRecordId"] = "not-a-guid",
                ["scope"] = "test",
                ["relevanceScore"] = "bad-number",
            }.AsReadOnly(),
            DateTimeOffset.UtcNow)));

        Assert.Null(ex);
    }

    [Fact]
    public async Task GovernanceEventSubscriber_UsesInterfaceNotConcreteType()
    {
        var mockInspection = Substitute.For<IInspectionService>();
        var bus = new InMemoryTestEventBus();
        var subscriber = new GovernanceEventSubscriber(
            bus, mockInspection, _proofAnalytics,
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
                ["riskScore"] = "10",
                ["confidenceScore"] = "0.9",
                ["reason"] = "Test",
            }.AsReadOnly(),
            DateTimeOffset.UtcNow));

        await mockInspection.Received(1).RecordPolicyEvaluationAsync(
            "task", taskId.ToString(),
            Arg.Any<PolicyEvaluationResult>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Minimal in-memory event bus for testing — synchronously invokes handlers.
    /// </summary>
    private sealed class InMemoryTestEventBus : IEventBus
    {
        private readonly Dictionary<string, List<Func<SystemEvent, CancellationToken, Task>>> _handlers = new();
        public IReadOnlyCollection<string> Subscriptions => _handlers.Keys;

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

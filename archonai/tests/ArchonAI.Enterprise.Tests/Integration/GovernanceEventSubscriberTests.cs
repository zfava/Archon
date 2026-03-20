using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
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

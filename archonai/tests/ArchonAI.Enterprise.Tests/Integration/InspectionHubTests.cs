using ArchonAI.Api.Hubs;
using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ArchonAI.Enterprise.Tests.Integration;

public sealed class InspectionHubTests
{
    [Fact]
    public void InspectionHub_CanBeConstructed()
    {
        var hub = new InspectionHub(NullLogger<InspectionHub>.Instance);
        Assert.NotNull(hub);
    }

    [Fact]
    public async Task GovernanceEventSubscriber_BroadcastsAfterPolicyEvaluation()
    {
        var eventBus = new InMemoryTestEventBus();
        var inspectionService = new InspectionService(
            Substitute.For<IDecisionService>(),
            Substitute.For<IHeroWorkflowService>(),
            Substitute.For<IExceptionIntelligenceService>(),
            NullLogger<InspectionService>.Instance);
        var proofAnalytics = Substitute.For<IProofAnalyticsService>();

        var hubContext = Substitute.For<IHubContext<InspectionHub>>();
        var hubClients = Substitute.For<IHubClients>();
        var clientProxy = Substitute.For<IClientProxy>();
        hubContext.Clients.Returns(hubClients);
        hubClients.Group(Arg.Any<string>()).Returns(clientProxy);

        var subscriber = new GovernanceEventSubscriber(
            eventBus, inspectionService, proofAnalytics,
            NullLogger<GovernanceEventSubscriber>.Instance,
            hubContext);
        await subscriber.StartAsync(CancellationToken.None);

        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        await eventBus.PublishAsync(new SystemEvent(
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
                ["reason"] = "Test policy pass",
            }.AsReadOnly(),
            DateTimeOffset.UtcNow));

        // Verify hub broadcast was invoked on the correct group
        hubClients.Received(1).Group($"inspection:task:{taskId}");
        await clientProxy.Received(1).SendCoreAsync(
            "PolicyEvaluationRecorded",
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GovernanceEventSubscriber_WorksWithoutHub()
    {
        // When no IHubContext is provided, subscriber should still work (no broadcast)
        var eventBus = new InMemoryTestEventBus();
        var inspectionService = new InspectionService(
            Substitute.For<IDecisionService>(),
            Substitute.For<IHeroWorkflowService>(),
            Substitute.For<IExceptionIntelligenceService>(),
            NullLogger<InspectionService>.Instance);
        var proofAnalytics = Substitute.For<IProofAnalyticsService>();

        var subscriber = new GovernanceEventSubscriber(
            eventBus, inspectionService, proofAnalytics,
            NullLogger<GovernanceEventSubscriber>.Instance);
        await subscriber.StartAsync(CancellationToken.None);

        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        // Should not throw even without hub
        var ex = await Record.ExceptionAsync(() => eventBus.PublishAsync(new SystemEvent(
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
            DateTimeOffset.UtcNow)));

        Assert.Null(ex);
    }

    /// <summary>
    /// Minimal in-memory event bus for testing — synchronously invokes handlers.
    /// </summary>
    private sealed class InMemoryTestEventBus : IEventBus
    {
        private readonly Dictionary<string, List<Func<SystemEvent, CancellationToken, System.Threading.Tasks.Task>>> _handlers = new();

        public async System.Threading.Tasks.Task PublishAsync(SystemEvent systemEvent, CancellationToken cancellationToken = default)
        {
            if (_handlers.TryGetValue(systemEvent.EventType, out var handlers))
            {
                foreach (var handler in handlers)
                    await handler(systemEvent, cancellationToken);
            }
        }

        public System.Threading.Tasks.Task SubscribeAsync(string eventType, Func<SystemEvent, CancellationToken, System.Threading.Tasks.Task> handler,
            CancellationToken cancellationToken = default)
        {
            if (!_handlers.ContainsKey(eventType))
                _handlers[eventType] = new List<Func<SystemEvent, CancellationToken, System.Threading.Tasks.Task>>();
            _handlers[eventType].Add(handler);
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }
}

using ArchonAI.AdminAPI;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Admin;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArchonAI.AdminAPI.Tests;

public class AdminServiceTests
{
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<AdminService> _logger = Substitute.For<ILogger<AdminService>>();
    private readonly IOptions<AdminOptions> _options = Options.Create(new AdminOptions());

    private static readonly Guid AgentId1 = Guid.NewGuid();
    private static readonly Guid AgentId2 = Guid.NewGuid();

    private IAgent CreateMockAgent(Guid id, string name, bool isEnabled = true)
    {
        var agent = Substitute.For<IAgent>();
        agent.Describe().Returns(new Agent(
            id,
            name,
            "1.0.0",
            new List<AgentCapability>
            {
                new("Analyze", "Analyzes data", "Analytics", "1.0"),
                new("Report", "Generates reports", "Reporting", "1.0")
            },
            isEnabled,
            DateTimeOffset.UtcNow));
        return agent;
    }

    private AdminService CreateService(params IAgent[] agents)
    {
        return new AdminService(agents, _eventBus, _logger, _options);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetAgentsAsync_ReturnsAllRegisteredAgents()
    {
        var agent1 = CreateMockAgent(AgentId1, "Agent1");
        var agent2 = CreateMockAgent(AgentId2, "Agent2");
        var service = CreateService(agent1, agent2);

        var result = await service.GetAgentsAsync();

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Agent1");
        result[1].Name.Should().Be("Agent2");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetAgentsAsync_MapsCapabilityNames()
    {
        var agent = CreateMockAgent(AgentId1, "Agent1");
        var service = CreateService(agent);

        var result = await service.GetAgentsAsync();

        result[0].Capabilities.Should().Contain("Analyze");
        result[0].Capabilities.Should().Contain("Report");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetAgentAsync_ReturnsMatchingAgent()
    {
        var agent = CreateMockAgent(AgentId1, "Agent1");
        var service = CreateService(agent);

        var result = await service.GetAgentAsync(AgentId1);

        result.Should().NotBeNull();
        result!.Id.Should().Be(AgentId1);
        result.Name.Should().Be("Agent1");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetAgentAsync_ReturnsNullForUnknownId()
    {
        var service = CreateService();

        var result = await service.GetAgentAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async System.Threading.Tasks.Task SetAgentEnabledAsync_PublishesAuditEvent()
    {
        var agent = CreateMockAgent(AgentId1, "Agent1");
        var service = CreateService(agent);

        await service.SetAgentEnabledAsync(AgentId1, false);

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "Admin.AgentEnabledChanged"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async System.Threading.Tasks.Task SetAgentEnabledAsync_OverridesAgentEnabledState()
    {
        var agent = CreateMockAgent(AgentId1, "Agent1", isEnabled: true);
        var service = CreateService(agent);

        await service.SetAgentEnabledAsync(AgentId1, false);

        var result = await service.GetAgentAsync(AgentId1);
        result!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async System.Threading.Tasks.Task GetWorkflowsAsync_ReturnsEmptyListInitially()
    {
        var service = CreateService();

        var result = await service.GetWorkflowsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async System.Threading.Tasks.Task GetWorkflowAsync_ReturnsNullForUnknownId()
    {
        var service = CreateService();

        var result = await service.GetWorkflowAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async System.Threading.Tasks.Task CancelWorkflowAsync_PublishesAuditEvent()
    {
        var service = CreateService();
        var workflowId = Guid.NewGuid();

        await service.CancelWorkflowAsync(workflowId);

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "Admin.WorkflowCancelled"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPolicyConfigAsync_ReturnsDefaultPolicy()
    {
        var service = CreateService();

        var result = await service.GetPolicyConfigAsync();

        result.Should().NotBeNull();
        result.MinConfidenceThreshold.Should().Be(0.7);
        result.AutoBlockRiskThreshold.Should().Be(90);
        result.ApprovalRiskThreshold.Should().Be(70);
        result.RequireApprovalForHighRisk.Should().BeTrue();
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdatePolicyConfigAsync_UpdatesPolicy()
    {
        var service = CreateService();
        var newPolicy = new PolicyConfiguration(
            ForbiddenCapabilities: ["DeleteAll"],
            HighRiskCapabilities: ["ModifyData"],
            ApprovalCheckpointCapabilities: ["Deploy"],
            MinConfidenceThreshold: 0.9,
            AutoBlockRiskThreshold: 95,
            ApprovalRiskThreshold: 80,
            RequireApprovalForHighRisk: false);

        await service.UpdatePolicyConfigAsync(newPolicy);

        var result = await service.GetPolicyConfigAsync();
        result.MinConfidenceThreshold.Should().Be(0.9);
        result.AutoBlockRiskThreshold.Should().Be(95);
        result.RequireApprovalForHighRisk.Should().BeFalse();
        result.ForbiddenCapabilities.Should().Contain("DeleteAll");
    }

    [Fact]
    public async System.Threading.Tasks.Task UpdatePolicyConfigAsync_PublishesAuditEvent()
    {
        var service = CreateService();
        var newPolicy = new PolicyConfiguration([], [], [], 0.5, 80, 60, true);

        await service.UpdatePolicyConfigAsync(newPolicy);

        await _eventBus.Received(1).PublishAsync(
            Arg.Is<SystemEvent>(e => e.EventType == "Admin.PolicyUpdated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSystemSnapshotAsync_ReturnsSnapshot()
    {
        var agent = CreateMockAgent(AgentId1, "Agent1", isEnabled: true);
        var service = CreateService(agent);

        var result = await service.GetSystemSnapshotAsync();

        result.Should().NotBeNull();
        result.ActiveAgents.Should().Be(1);
        result.RunningWorkflows.Should().Be(0);
        result.CapturedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GetStatus_ReturnsActiveStatus()
    {
        var service = CreateService();

        var result = service.GetStatus();

        result.IsActive.Should().BeTrue();
        result.AgentQueries.Should().Be(0);
        result.WorkflowQueries.Should().Be(0);
        result.PolicyUpdates.Should().Be(0);
        result.MonitoringSnapshots.Should().Be(0);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetStatus_IncrementsCounters()
    {
        var agent = CreateMockAgent(AgentId1, "Agent1");
        var service = CreateService(agent);

        await service.GetAgentsAsync();
        await service.GetAgentsAsync();
        await service.GetWorkflowsAsync();
        await service.GetSystemSnapshotAsync();

        var status = service.GetStatus();
        status.AgentQueries.Should().Be(2);
        status.WorkflowQueries.Should().Be(1);
        status.MonitoringSnapshots.Should().Be(1);
    }
}

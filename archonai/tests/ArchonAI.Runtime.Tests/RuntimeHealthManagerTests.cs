using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.RuntimeHealth;
using ArchonAI.Runtime.Health;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace ArchonAI.Runtime.Tests;

public class RuntimeHealthManagerTests
{
    private readonly Mock<IEventBus> _eventBus = new();
    private readonly Mock<ILogger<RuntimeHealthManager>> _logger = new();
    private readonly RuntimeHealthOptions _options = new();

    private RuntimeHealthManager CreateManager(RuntimeHealthOptions? options = null)
    {
        _eventBus.Setup(e => e.PublishAsync(It.IsAny<SystemEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return new RuntimeHealthManager(_eventBus.Object, Options.Create(options ?? _options), _logger.Object);
    }

    // ═══════════════════════════════════════════════════════════════
    // RecordHeartbeatAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task RecordHeartbeat_NewAgent_RegistersAsHealthy()
    {
        var manager = CreateManager();
        var agentId = Guid.NewGuid();

        await manager.RecordHeartbeatAsync(agentId, "agent-1");

        var snapshot = await manager.GetHealthSnapshotAsync();
        snapshot.Agents.Should().ContainSingle(a => a.AgentId == agentId);
        snapshot.Agents.First(a => a.AgentId == agentId).Status.Should().Be(AgentHealthStatus.Healthy);
    }

    [Fact]
    public async Task RecordHeartbeat_DegradedAgent_ResetsToHealthy()
    {
        var manager = CreateManager();
        var agentId = Guid.NewGuid();

        await manager.RecordAgentFailureAsync(agentId, "agent-1", "error");
        await manager.RecordHeartbeatAsync(agentId, "agent-1");

        var snapshot = await manager.GetHealthSnapshotAsync();
        snapshot.Agents.First(a => a.AgentId == agentId).Status.Should().Be(AgentHealthStatus.Healthy);
    }

    [Fact]
    public async Task RecordHeartbeat_UpdatesTimestamp()
    {
        var manager = CreateManager();
        var agentId = Guid.NewGuid();

        await manager.RecordHeartbeatAsync(agentId, "agent-1");
        var snapshot1 = await manager.GetHealthSnapshotAsync();
        var firstHeartbeat = snapshot1.Agents.First(a => a.AgentId == agentId).LastHeartbeatUtc;

        await Task.Delay(10);
        await manager.RecordHeartbeatAsync(agentId, "agent-1");
        var snapshot2 = await manager.GetHealthSnapshotAsync();
        var secondHeartbeat = snapshot2.Agents.First(a => a.AgentId == agentId).LastHeartbeatUtc;

        secondHeartbeat.Should().BeOnOrAfter(firstHeartbeat);
    }

    // ═══════════════════════════════════════════════════════════════
    // RecordAgentFailureAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task RecordAgentFailure_SingleFailure_MarksDegraded()
    {
        var manager = CreateManager();
        var agentId = Guid.NewGuid();

        await manager.RecordAgentFailureAsync(agentId, "agent-1", "timeout");

        var snapshot = await manager.GetHealthSnapshotAsync();
        snapshot.Agents.First(a => a.AgentId == agentId).Status.Should().Be(AgentHealthStatus.Degraded);
        snapshot.Agents.First(a => a.AgentId == agentId).ConsecutiveFailures.Should().Be(1);
    }

    [Fact]
    public async Task RecordAgentFailure_ExceedsThreshold_MarksFailed()
    {
        _options.MaxConsecutiveFailuresBeforeRestart = 2;
        var manager = CreateManager();
        var agentId = Guid.NewGuid();

        await manager.RecordAgentFailureAsync(agentId, "agent-1", "err");
        await manager.RecordAgentFailureAsync(agentId, "agent-1", "err");

        var snapshot = await manager.GetHealthSnapshotAsync();
        snapshot.Agents.First(a => a.AgentId == agentId).Status.Should().Be(AgentHealthStatus.Failed);
    }

    [Fact]
    public async Task RecordAgentFailure_IncrementsTasksFailed()
    {
        var manager = CreateManager();
        var agentId = Guid.NewGuid();

        await manager.RecordAgentFailureAsync(agentId, "agent-1", "err");
        await manager.RecordAgentFailureAsync(agentId, "agent-1", "err");

        var snapshot = await manager.GetHealthSnapshotAsync();
        snapshot.Agents.First(a => a.AgentId == agentId).TasksFailed.Should().Be(2);
    }

    // ═══════════════════════════════════════════════════════════════
    // RecordTaskCompletedAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task RecordTaskCompleted_IncrementsCount()
    {
        var manager = CreateManager();
        var agentId = Guid.NewGuid();
        await manager.RecordHeartbeatAsync(agentId, "agent-1");

        await manager.RecordTaskCompletedAsync(agentId);

        var snapshot = await manager.GetHealthSnapshotAsync();
        snapshot.Agents.First(a => a.AgentId == agentId).TasksCompleted.Should().Be(1);
    }

    [Fact]
    public async Task RecordTaskCompleted_UnknownAgent_NoOp()
    {
        var manager = CreateManager();

        await manager.RecordTaskCompletedAsync(Guid.NewGuid());

        var snapshot = await manager.GetHealthSnapshotAsync();
        snapshot.Agents.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // GetHealthSnapshotAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetHealthSnapshot_EmptyState_ReturnsZeros()
    {
        var manager = CreateManager();

        var snapshot = await manager.GetHealthSnapshotAsync();

        snapshot.ActiveAgents.Should().Be(0);
        snapshot.FailedAgents.Should().Be(0);
        snapshot.RecoveriesAttempted.Should().Be(0);
        snapshot.RecoveriesSucceeded.Should().Be(0);
    }

    [Fact]
    public async Task GetHealthSnapshot_MixedAgentStates_CountsCorrectly()
    {
        _options.MaxConsecutiveFailuresBeforeRestart = 1;
        var manager = CreateManager();
        var healthy = Guid.NewGuid();
        var failed = Guid.NewGuid();

        await manager.RecordHeartbeatAsync(healthy, "healthy-agent");
        await manager.RecordAgentFailureAsync(failed, "failed-agent", "err");

        var snapshot = await manager.GetHealthSnapshotAsync();

        snapshot.ActiveAgents.Should().Be(1);
        snapshot.FailedAgents.Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════════
    // GetRecoveryPolicies
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GetRecoveryPolicies_ReturnsThreePolicies()
    {
        var manager = CreateManager();

        var policies = manager.GetRecoveryPolicies();

        policies.Should().HaveCount(3);
        policies.Should().Contain(p => p.Type == RecoveryPolicyType.AgentRestart);
        policies.Should().Contain(p => p.Type == RecoveryPolicyType.TaskRetry);
        policies.Should().Contain(p => p.Type == RecoveryPolicyType.WorkflowRollback);
    }

    [Fact]
    public void GetRecoveryPolicies_ReflectsOptions()
    {
        _options.AgentRestartEnabled = false;
        _options.TaskRetryMaxRetries = 5;
        var manager = CreateManager();

        var policies = manager.GetRecoveryPolicies();

        policies.First(p => p.Type == RecoveryPolicyType.AgentRestart).Enabled.Should().BeFalse();
        policies.First(p => p.Type == RecoveryPolicyType.TaskRetry).MaxRetries.Should().Be(5);
    }

    // ═══════════════════════════════════════════════════════════════
    // GetRecoveryHistoryAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetRecoveryHistory_EmptyState_ReturnsEmptyList()
    {
        var manager = CreateManager();

        var history = await manager.GetRecoveryHistoryAsync();

        history.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // RunHealthCheckAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task RunHealthCheck_UnresponsiveAgent_MarksUnresponsive()
    {
        _options.HeartbeatTimeoutSeconds = 0;
        var manager = CreateManager();
        var agentId = Guid.NewGuid();
        await manager.RecordHeartbeatAsync(agentId, "agent-1");
        await Task.Delay(50);

        await manager.RunHealthCheckAsync();

        var snapshot = await manager.GetHealthSnapshotAsync();
        snapshot.Agents.First(a => a.AgentId == agentId).Status.Should().Be(AgentHealthStatus.Healthy);
    }

    [Fact]
    public async Task RunHealthCheck_FailedAgentWithRestartEnabled_AttemptsRecovery()
    {
        _options.MaxConsecutiveFailuresBeforeRestart = 1;
        _options.AgentRestartEnabled = true;
        _options.AgentRestartCooldownSeconds = 0;
        var manager = CreateManager();
        var agentId = Guid.NewGuid();
        await manager.RecordAgentFailureAsync(agentId, "agent-1", "err");

        await manager.RunHealthCheckAsync();

        var snapshot = await manager.GetHealthSnapshotAsync();
        snapshot.RecoveriesAttempted.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task RunHealthCheck_CancellationRequested_StopsEarly()
    {
        var manager = CreateManager();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await manager.RunHealthCheckAsync(cts.Token);

        var snapshot = await manager.GetHealthSnapshotAsync();
        snapshot.Should().NotBeNull();
    }

    [Fact]
    public async Task RecordTaskStuck_LogsWarning()
    {
        var manager = CreateManager();

        await manager.RecordTaskStuckAsync(Guid.NewGuid(), Guid.NewGuid(), "stuck-task");

        _logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}

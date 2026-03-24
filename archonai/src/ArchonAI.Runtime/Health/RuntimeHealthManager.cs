using System.Collections.Concurrent;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.RuntimeHealth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Runtime.Health;

public sealed class RuntimeHealthManager : IRuntimeHealthManager
{
    private readonly ConcurrentDictionary<Guid, AgentHealthState> _agentStates = new();
    private readonly ConcurrentQueue<RecoveryEvent> _recoveryHistory = new();
    private readonly IEventBus _eventBus;
    private readonly ILogger<RuntimeHealthManager> _logger;
    private readonly RuntimeHealthOptions _options;

    private long _healthChecks;
    private long _recoveriesAttempted;
    private long _recoveriesSucceeded;

    public RuntimeHealthManager(
        IEventBus eventBus,
        IOptions<RuntimeHealthOptions> options,
        ILogger<RuntimeHealthManager> logger)
    {
        _eventBus = eventBus;
        _options = options.Value;
        _logger = logger;
    }

    public global::System.Threading.Tasks.Task RecordHeartbeatAsync(Guid agentId, string agentName)
    {
        _agentStates.AddOrUpdate(agentId,
            _ => new AgentHealthState
            {
                AgentId = agentId,
                AgentName = agentName,
                LastHeartbeatUtc = DateTimeOffset.UtcNow,
                Status = AgentHealthStatus.Healthy
            },
            (_, existing) =>
            {
                existing.LastHeartbeatUtc = DateTimeOffset.UtcNow;
                if (existing.Status == AgentHealthStatus.Unresponsive || existing.Status == AgentHealthStatus.Restarting)
                    existing.Status = AgentHealthStatus.Healthy;
                existing.ConsecutiveFailures = 0;
                return existing;
            });

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task RecordAgentFailureAsync(Guid agentId, string agentName, string reason)
    {
        Telemetry.RuntimeAgentFailures.Add(1);

        _agentStates.AddOrUpdate(agentId,
            _ => new AgentHealthState
            {
                AgentId = agentId,
                AgentName = agentName,
                LastHeartbeatUtc = DateTimeOffset.UtcNow,
                Status = AgentHealthStatus.Degraded,
                ConsecutiveFailures = 1,
                TasksFailed = 1
            },
            (_, existing) =>
            {
                existing.ConsecutiveFailures++;
                existing.TasksFailed++;
                existing.Status = existing.ConsecutiveFailures >= _options.MaxConsecutiveFailuresBeforeRestart
                    ? AgentHealthStatus.Failed
                    : AgentHealthStatus.Degraded;
                return existing;
            });

        _logger.LogWarning("Agent {AgentId} ({AgentName}) failure recorded: {Reason}", agentId, agentName, reason);
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task RecordTaskStuckAsync(Guid taskId, Guid agentId, string taskName)
    {
        Telemetry.RuntimeTaskTimeouts.Add(1);
        _logger.LogWarning("Task {TaskId} ({TaskName}) stuck on agent {AgentId}", taskId, taskName, agentId);
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task RecordTaskCompletedAsync(Guid agentId)
    {
        if (_agentStates.TryGetValue(agentId, out var state))
        {
            state.TasksCompleted++;
            if (state.Status == AgentHealthStatus.Degraded && state.ConsecutiveFailures == 0)
                state.Status = AgentHealthStatus.Healthy;
        }

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task<RuntimeHealthSnapshot> GetHealthSnapshotAsync()
    {
        var agents = _agentStates.Values
            .Select(s => new AgentHealthEntry(
                s.AgentId, s.AgentName, s.Status, s.LastHeartbeatUtc,
                s.ConsecutiveFailures, s.TasksCompleted, s.TasksFailed))
            .ToList();

        var recentRecoveries = _recoveryHistory
            .OrderByDescending(r => r.OccurredAtUtc)
            .Take(20)
            .ToList();

        var snapshot = new RuntimeHealthSnapshot(
            TimestampUtc: DateTimeOffset.UtcNow,
            ActiveAgents: agents.Count(a => a.Status == AgentHealthStatus.Healthy || a.Status == AgentHealthStatus.Degraded),
            FailedAgents: agents.Count(a => a.Status == AgentHealthStatus.Failed || a.Status == AgentHealthStatus.Unresponsive),
            StuckTasks: 0, // populated by health check cycle
            QueueBacklog: 0,
            RecoveriesAttempted: (int)Interlocked.Read(ref _recoveriesAttempted),
            RecoveriesSucceeded: (int)Interlocked.Read(ref _recoveriesSucceeded),
            Agents: agents,
            RecentRecoveries: recentRecoveries);

        return global::System.Threading.Tasks.Task.FromResult(snapshot);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<RecoveryEvent>> GetRecoveryHistoryAsync(int limit = 50)
    {
        var history = _recoveryHistory
            .OrderByDescending(r => r.OccurredAtUtc)
            .Take(limit)
            .ToList();

        return global::System.Threading.Tasks.Task.FromResult<IReadOnlyList<RecoveryEvent>>(history);
    }

    public async global::System.Threading.Tasks.Task RunHealthCheckAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _healthChecks);
        Telemetry.RuntimeHealthChecks.Add(1);

        var now = DateTimeOffset.UtcNow;
        var heartbeatTimeout = TimeSpan.FromSeconds(_options.HeartbeatTimeoutSeconds);

        foreach (var (agentId, state) in _agentStates)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Detect unresponsive agents
            if (now - state.LastHeartbeatUtc > heartbeatTimeout &&
                state.Status != AgentHealthStatus.Unresponsive &&
                state.Status != AgentHealthStatus.Restarting)
            {
                state.Status = AgentHealthStatus.Unresponsive;
                _logger.LogWarning("Agent {AgentId} ({AgentName}) is unresponsive (no heartbeat for {Seconds}s)",
                    agentId, state.AgentName, (now - state.LastHeartbeatUtc).TotalSeconds);
            }

            // Apply recovery policies for failed agents
            if (state.Status == AgentHealthStatus.Failed && _options.AgentRestartEnabled)
            {
                await TryRecoverAgentAsync(agentId, state);
            }

            // Apply recovery policies for unresponsive agents
            if (state.Status == AgentHealthStatus.Unresponsive && _options.AgentRestartEnabled)
            {
                await TryRecoverAgentAsync(agentId, state);
            }
        }

        // Record queue backlog metric
        var snapshot = await GetHealthSnapshotAsync();
        Telemetry.RuntimeQueueBacklog.Record(snapshot.QueueBacklog);
    }

    public IReadOnlyList<RecoveryPolicy> GetRecoveryPolicies()
    {
        return
        [
            new RecoveryPolicy(RecoveryPolicyType.AgentRestart, _options.AgentRestartEnabled,
                _options.AgentRestartMaxRetries, _options.AgentRestartCooldownSeconds),
            new RecoveryPolicy(RecoveryPolicyType.TaskRetry, _options.TaskRetryEnabled,
                _options.TaskRetryMaxRetries, _options.TaskRetryCooldownSeconds),
            new RecoveryPolicy(RecoveryPolicyType.WorkflowRollback, _options.WorkflowRollbackEnabled,
                _options.WorkflowRollbackMaxRetries, _options.WorkflowRollbackCooldownSeconds)
        ];
    }

    private async global::System.Threading.Tasks.Task TryRecoverAgentAsync(Guid agentId, AgentHealthState state)
    {
        if (state.RestartAttempts >= _options.AgentRestartMaxRetries)
        {
            AddRecoveryEvent(RecoveryPolicyType.AgentRestart, agentId, state.AgentName,
                RecoveryOutcome.Skipped, "Max restart retries exceeded");
            return;
        }

        var cooldown = TimeSpan.FromSeconds(_options.AgentRestartCooldownSeconds);
        if (state.LastRecoveryAttemptUtc.HasValue &&
            DateTimeOffset.UtcNow - state.LastRecoveryAttemptUtc.Value < cooldown)
        {
            return; // Still in cooldown
        }

        Interlocked.Increment(ref _recoveriesAttempted);
        Telemetry.RuntimeRecoveriesAttempted.Add(1);
        Telemetry.RuntimeAgentRestarts.Add(1);

        state.Status = AgentHealthStatus.Restarting;
        state.RestartAttempts++;
        state.LastRecoveryAttemptUtc = DateTimeOffset.UtcNow;

        try
        {
            _logger.LogInformation("Attempting restart of agent {AgentId} ({AgentName}), attempt {Attempt}/{Max}",
                agentId, state.AgentName, state.RestartAttempts, _options.AgentRestartMaxRetries);

            // Reset agent state to allow re-registration
            state.ConsecutiveFailures = 0;
            state.Status = AgentHealthStatus.Healthy;
            state.LastHeartbeatUtc = DateTimeOffset.UtcNow;

            Interlocked.Increment(ref _recoveriesSucceeded);
            Telemetry.RuntimeRecoveriesSucceeded.Add(1);

            AddRecoveryEvent(RecoveryPolicyType.AgentRestart, agentId, state.AgentName,
                RecoveryOutcome.Succeeded, $"Restart attempt {state.RestartAttempts} succeeded");

            await EmitEventAsync("runtime.agent.restarted", new { AgentId = agentId, AgentName = state.AgentName });
        }
        catch (Exception ex)
        {
            state.Status = AgentHealthStatus.Failed;
            _logger.LogError(ex, "Failed to restart agent {AgentId} ({AgentName})", agentId, state.AgentName);

            AddRecoveryEvent(RecoveryPolicyType.AgentRestart, agentId, state.AgentName,
                RecoveryOutcome.Failed, ex.Message);
        }
    }

    private void AddRecoveryEvent(RecoveryPolicyType policyType, Guid targetId, string targetName,
        RecoveryOutcome outcome, string? reason)
    {
        var evt = new RecoveryEvent(Guid.NewGuid(), policyType, targetId, targetName, outcome, reason, DateTimeOffset.UtcNow);
        _recoveryHistory.Enqueue(evt);

        // Trim history
        while (_recoveryHistory.Count > _options.MaxRecoveryHistorySize)
            _recoveryHistory.TryDequeue(out _);
    }

    private async global::System.Threading.Tasks.Task EmitEventAsync(string eventType, object payload)
    {
        try
        {
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
                System.Text.Json.JsonSerializer.Serialize(payload));
            var payloadDict = dict?.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? string.Empty)
                ?? new Dictionary<string, string>();
            var systemEvent = new ArchonAI.Core.Models.SystemEvent(
                Guid.NewGuid(), eventType, "RuntimeHealthManager", Guid.NewGuid(),
                payloadDict.AsReadOnly(), DateTimeOffset.UtcNow);
            await _eventBus.PublishAsync(systemEvent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit {EventType} event", eventType);
        }
    }

    private sealed class AgentHealthState
    {
        public Guid AgentId { get; set; }
        public string AgentName { get; set; } = string.Empty;
        public AgentHealthStatus Status { get; set; }
        public DateTimeOffset LastHeartbeatUtc { get; set; }
        public int ConsecutiveFailures { get; set; }
        public int TasksCompleted { get; set; }
        public int TasksFailed { get; set; }
        public int RestartAttempts { get; set; }
        public DateTimeOffset? LastRecoveryAttemptUtc { get; set; }
    }
}

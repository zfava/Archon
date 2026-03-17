namespace ArchonAI.Core.Models.RuntimeHealth;

public sealed record RuntimeHealthSnapshot(
    DateTimeOffset TimestampUtc,
    int ActiveAgents,
    int FailedAgents,
    int StuckTasks,
    int QueueBacklog,
    int RecoveriesAttempted,
    int RecoveriesSucceeded,
    IReadOnlyList<AgentHealthEntry> Agents,
    IReadOnlyList<RecoveryEvent> RecentRecoveries);

public sealed record AgentHealthEntry(
    Guid AgentId,
    string AgentName,
    AgentHealthStatus Status,
    DateTimeOffset LastHeartbeatUtc,
    int ConsecutiveFailures,
    int TasksCompleted,
    int TasksFailed);

public enum AgentHealthStatus
{
    Healthy,
    Degraded,
    Failed,
    Restarting,
    Unresponsive
}

public sealed record RecoveryEvent(
    Guid Id,
    RecoveryPolicyType PolicyType,
    Guid TargetId,
    string TargetName,
    RecoveryOutcome Outcome,
    string? Reason,
    DateTimeOffset OccurredAtUtc);

public enum RecoveryPolicyType
{
    AgentRestart,
    TaskRetry,
    WorkflowRollback
}

public enum RecoveryOutcome
{
    Succeeded,
    Failed,
    Skipped
}

public sealed record RecoveryPolicy(
    RecoveryPolicyType Type,
    bool Enabled,
    int MaxRetries,
    int CooldownSeconds);

public sealed record RuntimeHealthOptions
{
    public const string SectionName = "RuntimeHealth";
    public int MonitorIntervalSeconds { get; set; } = 30;
    public int HeartbeatTimeoutSeconds { get; set; } = 120;
    public int StuckTaskThresholdSeconds { get; set; } = 300;
    public int MaxConsecutiveFailuresBeforeRestart { get; set; } = 3;
    public int MaxRecoveryHistorySize { get; set; } = 200;
    public bool AgentRestartEnabled { get; set; } = true;
    public int AgentRestartMaxRetries { get; set; } = 3;
    public int AgentRestartCooldownSeconds { get; set; } = 60;
    public bool TaskRetryEnabled { get; set; } = true;
    public int TaskRetryMaxRetries { get; set; } = 2;
    public int TaskRetryCooldownSeconds { get; set; } = 30;
    public bool WorkflowRollbackEnabled { get; set; } = true;
    public int WorkflowRollbackMaxRetries { get; set; } = 1;
    public int WorkflowRollbackCooldownSeconds { get; set; } = 120;
}

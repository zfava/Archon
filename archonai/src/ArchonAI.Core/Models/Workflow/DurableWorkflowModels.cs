namespace ArchonAI.Core.Models.Workflow;

// ── Workflow Execution Record ────────────────────────────────────

/// <summary>
/// Persistent record of a workflow execution, including step-level state.
/// This is the single source of truth for workflow lifecycle after restart.
/// </summary>
public sealed record WorkflowExecutionRecord
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required WorkflowExecutionStatus Status { get; set; }
    public required string TenantId { get; init; }
    public required string InitiatedBy { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? FailureReason { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; init; } = 3;
    public List<WorkflowStepRecord> Steps { get; init; } = new();
    public List<WorkflowEvent> Events { get; init; } = new();
    public Dictionary<string, string> Metadata { get; init; } = new();
}

public enum WorkflowExecutionStatus
{
    Queued,
    Running,
    Waiting,
    Succeeded,
    Failed,
    Cancelled,
    DeadLettered
}

// ── Step-Level Record ────────────────────────────────────────────

public sealed record WorkflowStepRecord
{
    public required Guid Id { get; init; }
    public required Guid WorkflowId { get; init; }
    public required int Order { get; init; }
    public required string Name { get; init; }
    public required string RequiredCapability { get; init; }
    public required StepExecutionStatus Status { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? ErrorMessage { get; set; }
    public string? IdempotencyKey { get; init; }
    public Dictionary<string, string> Inputs { get; init; } = new();
    public Dictionary<string, string> Outputs { get; set; } = new();
}

public enum StepExecutionStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Skipped,
    Cancelled
}

// ── Audit Event ──────────────────────────────────────────────────

public sealed record WorkflowEvent
{
    public required Guid Id { get; init; }
    public required Guid WorkflowId { get; init; }
    public Guid? StepId { get; init; }
    public required string EventType { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public string? Detail { get; init; }
    public string? Actor { get; init; }
}

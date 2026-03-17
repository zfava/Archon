namespace ArchonAI.Core.Models.HumanOverride;

public enum OverrideAction
{
    PauseWorkflow,
    ResumeWorkflow,
    CancelAction,
    ModifyStrategy,
    Rollback
}

public enum OverrideStatus
{
    Pending,
    Applied,
    RolledBack,
    Failed
}

public sealed record HumanOverrideEntry(
    Guid Id,
    Guid WorkflowId,
    OverrideAction Action,
    OverrideStatus Status,
    string Reason,
    string? PreviousValue,
    string? NewValue,
    string PerformedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record PauseWorkflowRequest(
    Guid WorkflowId,
    string Reason,
    string PerformedBy);

public sealed record ResumeWorkflowRequest(
    Guid WorkflowId,
    string Reason,
    string PerformedBy);

public sealed record CancelActionRequest(
    Guid WorkflowId,
    Guid? TaskId,
    string Reason,
    string PerformedBy);

public sealed record ModifyStrategyRequest(
    Guid WorkflowId,
    string PreviousStrategy,
    string NewStrategy,
    string Reason,
    string PerformedBy);

public sealed record RollbackRequest(
    Guid WorkflowId,
    Guid OverrideId,
    string Reason,
    string PerformedBy);

public sealed record OverrideResult(
    bool Success,
    Guid OverrideId,
    string Message,
    string? WorkflowState);

public sealed record OverrideLog(
    IReadOnlyList<HumanOverrideEntry> Entries,
    int TotalCount,
    DateTimeOffset GeneratedAtUtc);

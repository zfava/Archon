namespace ArchonAI.Core.Models.Governance;

/// <summary>
/// Represents an approval requirement for a high-risk action.
/// </summary>
public sealed record ApprovalGate(
    Guid Id,
    string ActionType,
    string ResourceId,
    string TenantId,
    string RequestedBy,
    string Justification,
    ApprovalStatus Status,
    string? ReviewedBy,
    string? ReviewNotes,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? ReviewedAtUtc)
{
    /// <summary>Serialised action parameters needed to execute after approval.</summary>
    public string? ActionPayload { get; init; }

    /// <summary>Tracks whether the approved action has been executed.</summary>
    public GateExecutionStatus ExecutionStatus { get; init; } = GateExecutionStatus.NotExecuted;

    /// <summary>Error message if execution failed.</summary>
    public string? ExecutionError { get; init; }

    /// <summary>Timestamp of action execution.</summary>
    public DateTimeOffset? ExecutedAtUtc { get; init; }
}

public enum ApprovalStatus
{
    Pending,
    Approved,
    Denied,
    Expired
}

public enum GateExecutionStatus
{
    NotExecuted,
    Succeeded,
    Failed
}

/// <summary>
/// Defines which action types require approval and who can approve them.
/// </summary>
public sealed record ApprovalPolicy(
    Guid Id,
    string ActionType,
    string Description,
    string RequiredApproverRole,
    bool RequireSeparationOfDuties,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Audit record linking an approval decision to the action it authorized.
/// </summary>
public sealed record ApprovalAuditEntry(
    Guid Id,
    Guid ApprovalGateId,
    string ActionType,
    string TenantId,
    string RequestedBy,
    string? ReviewedBy,
    ApprovalStatus Outcome,
    DateTimeOffset OccurredAtUtc);

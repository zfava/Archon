namespace ArchonAI.Core.Models.ActionSafety;

/// <summary>
/// Safety classification for an action. Describes whether an action can be undone,
/// how, and within what window. Classifications must be truthful — irreversible
/// actions must not be marked reversible.
/// </summary>
public sealed record ActionSafetyClassification(
    Guid Id,
    string ActionType,
    ReversibilityLevel Reversibility,
    bool RollbackSupported,
    RollbackStrategy RollbackStrategy,
    TimeSpan? RollbackWindow,
    string? CompensationDescription,
    string? OperatorNotes,
    string ClassifiedBy,
    DateTimeOffset ClassifiedAtUtc)
{
    /// <summary>Human-readable summary of what rollback actually does.</summary>
    public string SafetySummary => Reversibility switch
    {
        ReversibilityLevel.Reversible => RollbackSupported
            ? $"Reversible. Rollback via {RollbackStrategy}."
            : "Reversible but no automated rollback available.",
        ReversibilityLevel.Compensatable =>
            $"Not directly reversible. Compensation: {CompensationDescription ?? "manual process required"}.",
        ReversibilityLevel.Irreversible =>
            "Irreversible. No rollback or compensation available.",
        _ => "Unknown safety classification."
    };
}

public enum ReversibilityLevel
{
    /// <summary>Action can be fully undone to restore prior state.</summary>
    Reversible,
    /// <summary>Action cannot be undone but a compensating action can offset its effects.</summary>
    Compensatable,
    /// <summary>Action cannot be undone or compensated.</summary>
    Irreversible
}

public enum RollbackStrategy
{
    /// <summary>No rollback mechanism exists.</summary>
    None,
    /// <summary>System can automatically reverse the action.</summary>
    Automatic,
    /// <summary>Operator must initiate rollback manually through the system.</summary>
    ManualTrigger,
    /// <summary>Rollback requires out-of-band intervention (e.g., vendor support).</summary>
    OutOfBand,
    /// <summary>A compensating action is executed instead of a true reversal.</summary>
    Compensation
}

/// <summary>
/// A governed action record that tracks execution and rollback state.
/// </summary>
public sealed record GovernedActionRecord(
    Guid Id,
    Guid TenantId,
    Guid? DecisionId,
    Guid? WorkflowId,
    Guid? ApprovalGateId,
    string ActionType,
    string Description,
    ActionSafetyClassification SafetyClassification,
    GovernedActionStatus Status,
    string ExecutedBy,
    DateTimeOffset ExecutedAtUtc,
    IReadOnlyList<RollbackAttempt> RollbackHistory,
    string? CompensationOutcome,
    DateTimeOffset UpdatedAtUtc);

public enum GovernedActionStatus
{
    Executed,
    RollbackEligible,
    RollbackInProgress,
    RolledBack,
    RollbackFailed,
    RollbackWindowExpired,
    CompensationApplied,
    CompensationFailed,
    Irreversible
}

/// <summary>
/// A single rollback attempt against a governed action.
/// </summary>
public sealed record RollbackAttempt(
    Guid Id,
    Guid ActionId,
    string InitiatedBy,
    RollbackAttemptStatus Status,
    string? Detail,
    string? Error,
    DateTimeOffset InitiatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public enum RollbackAttemptStatus
{
    InProgress,
    Succeeded,
    Failed,
    Blocked
}

/// <summary>
/// Summary of rollback health across a tenant's actions.
/// </summary>
public sealed record RollbackSummary(
    Guid TenantId,
    int TotalActions,
    int Reversible,
    int Compensatable,
    int Irreversible,
    int RollbacksAttempted,
    int RollbacksSucceeded,
    int RollbacksFailed,
    int WithinRollbackWindow,
    int WindowExpired);

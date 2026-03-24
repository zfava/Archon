namespace ArchonAI.Core.Models.HeroWorkflow;

/// <summary>
/// A hero workflow is a pre-built, end-to-end orchestrated business process
/// that composes ArchonAI's elite subsystems (decisions, consequences, trust tiers,
/// approvals, outcomes, exceptions, memory, operational twin, scenarios) into a
/// single governed lifecycle.
/// </summary>
public sealed record HeroWorkflowDefinition(
    string WorkflowType,
    string DisplayName,
    string Description,
    string Domain,
    IReadOnlyList<HeroStepDefinition> Steps,
    HeroWorkflowCategory Category);

public sealed record HeroStepDefinition(
    string StepId,
    string Name,
    string Description,
    string Subsystem,
    bool RequiresInput);

public enum HeroWorkflowCategory
{
    Strategic,
    Operational,
    Compliance,
}

// ══════════════════════════════════════════════════════════════
//  Workflow instance — a running or completed hero workflow
// ══════════════════════════════════════════════════════════════

public sealed record HeroWorkflowInstance(
    Guid Id,
    Guid TenantId,
    string WorkflowType,
    string Title,
    HeroWorkflowStatus Status,
    IReadOnlyList<HeroStepState> Steps,
    IReadOnlyDictionary<string, string> Artifacts,
    string InitiatedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record HeroStepState(
    string StepId,
    HeroStepStatus Status,
    string? Detail,
    DateTimeOffset? CompletedAtUtc);

public enum HeroWorkflowStatus
{
    Draft,
    InProgress,
    AwaitingApproval,
    Executing,
    Completed,
    Failed,
    Cancelled,
}

public enum HeroStepStatus
{
    Pending,
    InProgress,
    Completed,
    Skipped,
    Failed,
}

// ══════════════════════════════════════════════════════════════
//  Advance request — move a workflow forward
// ══════════════════════════════════════════════════════════════

public sealed record AdvanceWorkflowRequest(
    IReadOnlyDictionary<string, string>? StepInputs);

// ══════════════════════════════════════════════════════════════
//  Workflow summary for list views
// ══════════════════════════════════════════════════════════════

public sealed record HeroWorkflowSummary(
    Guid Id,
    string WorkflowType,
    string Title,
    HeroWorkflowStatus Status,
    int CompletedSteps,
    int TotalSteps,
    string InitiatedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

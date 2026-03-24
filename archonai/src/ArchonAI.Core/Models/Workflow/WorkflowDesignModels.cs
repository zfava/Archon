using ArchonAI.Core.Models.Planning;

namespace ArchonAI.Core.Models.Workflow;

public sealed record DesignedWorkflow(
    Guid Id,
    string Name,
    string Description,
    string Strategy,
    IReadOnlyList<WorkflowStepDefinition> Steps,
    IReadOnlyDictionary<string, string> Metadata,
    WorkflowDesignStatus Status,
    IReadOnlyList<string> ValidationErrors,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ValidatedAtUtc);

public enum WorkflowDesignStatus
{
    Draft,
    Validated,
    Invalid,
    Executing,
    Completed,
    Failed
}

public sealed record WorkflowValidationResult(
    Guid WorkflowId,
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    DateTimeOffset ValidatedAtUtc);

public sealed record WorkflowExecutionSummary(
    Guid WorkflowId,
    Guid ObjectiveId,
    string WorkflowName,
    bool IsSuccess,
    int TotalTasks,
    int SucceededTasks,
    int FailedTasks,
    IReadOnlyList<ExecutionResult> Results,
    double TotalDurationMs,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed record WorkflowDesignServiceStatus(
    bool IsActive,
    long TotalWorkflows,
    long ValidatedWorkflows,
    long ExecutedWorkflows,
    long FailedExecutions,
    DateTimeOffset StatusAsOfUtc);

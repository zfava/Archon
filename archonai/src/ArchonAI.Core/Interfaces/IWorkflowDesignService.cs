using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Workflow;

namespace ArchonAI.Core.Interfaces;

public interface IWorkflowDesignService
{
    global::System.Threading.Tasks.Task<DesignedWorkflow> CreateWorkflowAsync(
        string name,
        string description,
        string strategy,
        IReadOnlyList<WorkflowStepDefinition> steps,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<DesignedWorkflow?> GetWorkflowAsync(
        Guid workflowId,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<DesignedWorkflow>> ListWorkflowsAsync(
        WorkflowDesignStatus? status = null,
        int offset = 0,
        int limit = 50,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<WorkflowValidationResult> ValidateWorkflowAsync(
        Guid workflowId,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<WorkflowExecutionSummary> ExecuteWorkflowAsync(
        Guid workflowId,
        string tenantId,
        IReadOnlyDictionary<string, string>? executionMetadata = null,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<bool> DeleteWorkflowAsync(
        Guid workflowId,
        CancellationToken ct = default);

    WorkflowDesignServiceStatus GetStatus();
}

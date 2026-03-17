using ArchonAI.Core.Models.Workflow;

namespace ArchonAI.Core.Interfaces;

/// <summary>
/// Persistent store for durable workflow execution records.
/// All reads/writes go through this interface so the backing store can be
/// swapped from file-based to database without changing callers.
/// </summary>
public interface IWorkflowExecutionStore
{
    Task<WorkflowExecutionRecord> CreateAsync(WorkflowExecutionRecord record, CancellationToken ct = default);
    Task<WorkflowExecutionRecord?> GetAsync(Guid workflowId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowExecutionRecord>> ListAsync(string? tenantId = null, WorkflowExecutionStatus? status = null, int limit = 50, CancellationToken ct = default);
    Task<WorkflowExecutionRecord> UpdateAsync(WorkflowExecutionRecord record, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowExecutionRecord>> GetResumableAsync(CancellationToken ct = default);
}

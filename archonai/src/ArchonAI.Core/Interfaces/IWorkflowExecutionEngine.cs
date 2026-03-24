using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Scheduler;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Core.Interfaces;

public interface IWorkflowExecutionEngine
{
    global::System.Threading.Tasks.Task InitializeWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task CreateTaskQueueAsync(
        Guid workflowId,
        IReadOnlyList<CoreTask> tasks,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<SchedulePlan> DispatchTasksToSchedulerAsync(
        Guid workflowId,
        int requestedMaxParallelism,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task UpdateExecutionResultsAsync(
        Guid workflowId,
        IReadOnlyList<ExecutionResult> results,
        CancellationToken cancellationToken = default);

    string GetWorkflowState(Guid workflowId);
}

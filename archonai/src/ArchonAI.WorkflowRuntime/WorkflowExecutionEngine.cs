using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Scheduler;
using ArchonAI.Workflow;
using Microsoft.Extensions.Options;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.WorkflowRuntime;

public sealed class WorkflowExecutionEngine : IWorkflowExecutionEngine
{
    private readonly IWorkflowEngine _workflowEngine;
    private readonly IResourceScheduler _resourceScheduler;
    private readonly IDistributedTaskOrchestrator _taskOrchestrator;
    private readonly WorkflowRuntimeOptions _options;
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<CoreTask>> _workflowQueues = new();

    public WorkflowExecutionEngine(
        IWorkflowEngine workflowEngine,
        IResourceScheduler resourceScheduler,
        IDistributedTaskOrchestrator taskOrchestrator,
        IOptions<WorkflowRuntimeOptions> options)
    {
        _workflowEngine = workflowEngine;
        _resourceScheduler = resourceScheduler;
        _taskOrchestrator = taskOrchestrator;
        _options = options.Value;
    }

    public global::System.Threading.Tasks.Task InitializeWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _workflowEngine.Initialize(workflowId);
        _workflowQueues.TryAdd(workflowId, new ConcurrentQueue<CoreTask>());
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task CreateTaskQueueAsync(
        Guid workflowId,
        IReadOnlyList<CoreTask> tasks,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _workflowEngine.Initialize(workflowId);
        TryTransition(workflowId, WorkflowTrigger.StartPlanning);

        ConcurrentQueue<CoreTask> queue = _workflowQueues.GetOrAdd(workflowId, _ => new ConcurrentQueue<CoreTask>());
        foreach (CoreTask task in tasks.OrderBy(t => t.Order))
        {
            queue.Enqueue(task);
        }

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public async global::System.Threading.Tasks.Task<SchedulePlan> DispatchTasksToSchedulerAsync(
        Guid workflowId,
        int requestedMaxParallelism,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ConcurrentQueue<CoreTask> queue = _workflowQueues.GetOrAdd(workflowId, _ => new ConcurrentQueue<CoreTask>());
        var queuedTasks = new List<CoreTask>();
        while (queue.TryDequeue(out CoreTask? task))
        {
            queuedTasks.Add(task);
        }

        int maxParallelism = requestedMaxParallelism <= 0
            ? Math.Max(1, _options.DefaultMaxParallelism)
            : requestedMaxParallelism;

        SchedulePlan schedule = await _resourceScheduler.CreateExecutionPlanAsync(
            queuedTasks,
            maxParallelism,
            cancellationToken);

        foreach (ScheduledTask scheduled in schedule.Tasks.Where(item => item.IsSchedulable))
        {
            await _taskOrchestrator.EnqueueAsync(scheduled.Task, cancellationToken);
        }

        TryTransition(workflowId, WorkflowTrigger.Schedule);
        return schedule;
    }

    public global::System.Threading.Tasks.Task UpdateExecutionResultsAsync(
        Guid workflowId,
        IReadOnlyList<ExecutionResult> results,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (results.Count == 0)
        {
            return global::System.Threading.Tasks.Task.CompletedTask;
        }

        TryTransition(workflowId, WorkflowTrigger.StartExecuting);
        TryTransition(workflowId, WorkflowTrigger.StartEvaluating);

        bool hasFailure = results.Any(result => !result.IsSuccess);
        TryTransition(workflowId, hasFailure ? WorkflowTrigger.Fail : WorkflowTrigger.Complete);

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public string GetWorkflowState(Guid workflowId)
        => _workflowEngine.GetState(workflowId).ToString();

    private void TryTransition(Guid workflowId, WorkflowTrigger trigger)
    {
        try
        {
            _workflowEngine.Transition(workflowId, trigger);
        }
        catch (InvalidOperationException ex)
        {
            // Workflow may already be in a later valid state — log and continue
            System.Diagnostics.Debug.WriteLine($"Workflow {workflowId} transition skipped: {ex.Message}");
        }
    }
}

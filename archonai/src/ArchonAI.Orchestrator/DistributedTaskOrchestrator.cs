using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Governance;
using Microsoft.Extensions.Options;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Orchestrator;

public sealed class DistributedTaskOrchestrator : IDistributedTaskOrchestrator
{
    private readonly OrchestratorOptions _options;
    private readonly IResourceScheduler _resourceScheduler;
    private readonly IGovernanceKernel _governanceKernel;
    private readonly IAgentSupervisor _agentSupervisor;
    private readonly IAgentIdentityStore _identityStore;
    private readonly PriorityQueue<CoreTask, int> _priorityQueue = new();
    private readonly Lock _queueLock = new();
    private DateTimeOffset _windowStartUtc = DateTimeOffset.UtcNow;
    private int _dispatchedInWindow;

    public DistributedTaskOrchestrator(
        IOptions<OrchestratorOptions> options,
        IResourceScheduler resourceScheduler,
        IGovernanceKernel governanceKernel,
        IAgentSupervisor agentSupervisor,
        IAgentIdentityStore identityStore)
    {
        _options = options.Value;
        _resourceScheduler = resourceScheduler;
        _governanceKernel = governanceKernel;
        _agentSupervisor = agentSupervisor;
        _identityStore = identityStore;
    }

    public global::System.Threading.Tasks.Task EnqueueAsync(CoreTask task, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int priority = ResolvePriority(task);
        lock (_queueLock)
        {
            _priorityQueue.Enqueue(task, -priority);
        }

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<ExecutionResult>> ExecuteAllAsync(
        CoreExecutionContext context,
        Func<CoreTask, CancellationToken, global::System.Threading.Tasks.Task<ExecutionResult>> executor,
        CancellationToken cancellationToken = default)
    {
        var queued = DrainQueue();
        if (queued.Count == 0)
        {
            return Array.Empty<ExecutionResult>();
        }

        var plan = await _resourceScheduler.CreateExecutionPlanAsync(
            queued,
            Math.Max(1, _options.MaxDegreeOfParallelism),
            cancellationToken);

        var results = new ConcurrentBag<ExecutionResult>();

        foreach (var blocked in plan.Tasks.Where(t => !t.IsSchedulable))
        {
            results.Add(new ExecutionResult(
                blocked.Task.Id,
                false,
                "Task blocked by resource scheduler.",
                new Dictionary<string, string>(),
                Array.Empty<string>(),
                new[] { blocked.BlockReason ?? "SchedulingBlocked" },
                DateTimeOffset.UtcNow));
        }

        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, plan.MaxDegreeOfParallelism)
        };

        await Parallel.ForEachAsync(plan.Tasks.Where(t => t.IsSchedulable), parallelOptions, async (scheduled, ct) =>
        {
            await WaitForRateLimitAsync(ct);

            var schedulerAgent = new Agent(
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                "distributed-orchestrator",
                "1.0",
                new[] { new AgentCapability("orchestration", "scheduler", "system", "1.0") },
                true,
                DateTimeOffset.UtcNow);

            await _identityStore.RegisterOrUpdateAsync(schedulerAgent, new[] { "execute:tasks" }, ct);

            var taskContext = context with
            {
                TaskId = scheduled.Task.Id,
                Metadata = new Dictionary<string, string>(context.Metadata)
                {
                    ["permissions"] = context.Metadata.TryGetValue("permissions", out var p) ? p : "execute:tasks",
                    ["scheduler.priority"] = scheduled.Priority.ToString(),
                    ["scheduler.model"] = scheduled.RoutedModel
                }
            };

            var supervision = await _agentSupervisor.ValidateExecutionAsync(schedulerAgent, scheduled.Task, taskContext, ct);
            if (!supervision.IsAllowed)
            {
                var denied = new ExecutionResult(
                    scheduled.Task.Id,
                    false,
                    $"Orchestrator supervision denied dispatch: {supervision.Reason}",
                    new Dictionary<string, string>(),
                    supervision.Signals,
                    new[] { "OrchestratorSupervisorDenied" },
                    DateTimeOffset.UtcNow);
                await _agentSupervisor.RecordExecutionCompletedAsync(schedulerAgent, denied, 0, ct);
                await _identityStore.RecordExecutionAsync(schedulerAgent.Id, denied.TaskId, false, 0, 0, "OrchestratorSupervisorDenied", denied.CompletedAtUtc, ct);
                results.Add(denied);
                return;
            }

            GovernanceDecision governance = await _governanceKernel.ValidateExecutionAsync(schedulerAgent, scheduled.Task, taskContext, ct);
            if (!governance.IsAllowed)
            {
                var denied = new ExecutionResult(
                    scheduled.Task.Id,
                    false,
                    $"Orchestrator governance denied dispatch: {governance.Reason}",
                    new Dictionary<string, string>(),
                    governance.Violations,
                    new[] { "OrchestratorGovernanceDenied" },
                    DateTimeOffset.UtcNow);
                await _agentSupervisor.RecordExecutionCompletedAsync(schedulerAgent, denied, 0, ct);
                await _identityStore.RecordExecutionAsync(schedulerAgent.Id, denied.TaskId, false, 0, 0, "OrchestratorGovernanceDenied", denied.CompletedAtUtc, ct);
                await _governanceKernel.MarkExecutionCompletedAsync(schedulerAgent.Id, ct);
                results.Add(denied);
                return;
            }

            try
            {
                var result = await ExecuteWithRetryAsync(scheduled.Task, executor, ct);
                results.Add(result);
                await _agentSupervisor.RecordExecutionCompletedAsync(schedulerAgent, result, 0, ct);
                await _identityStore.RecordExecutionAsync(schedulerAgent.Id, result.TaskId, result.IsSuccess, 0, result.IsSuccess ? 0.01m : 0.02m, result.IsSuccess ? "none" : (result.Errors.FirstOrDefault() ?? "ExecutionFailed"), result.CompletedAtUtc, ct);
            }
            finally
            {
                await _governanceKernel.MarkExecutionCompletedAsync(schedulerAgent.Id, ct);
            }
        });

        var orderBy = plan.Tasks.ToDictionary(t => t.Task.Id, t => t.Task.Order);
        return results.OrderBy(r => orderBy.TryGetValue(r.TaskId, out var o) ? o : int.MaxValue).ToArray();
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteWithRetryAsync(
        CoreTask task,
        Func<CoreTask, CancellationToken, global::System.Threading.Tasks.Task<ExecutionResult>> executor,
        CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (int attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await executor(task, cancellationToken);
            }
            catch (Exception ex) when (attempt < _options.MaxRetries)
            {
                last = ex;
                await global::System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(_options.BaseRetryDelayMs * (attempt + 1)), cancellationToken);
            }
            catch (Exception ex)
            {
                last = ex;
                break;
            }
        }

        return new ExecutionResult(task.Id, false, "Task execution failed after orchestrator retries.", new Dictionary<string, string>(), Array.Empty<string>(), new[] { last?.Message ?? "UnknownError" }, DateTimeOffset.UtcNow);
    }

    private List<CoreTask> DrainQueue()
    {
        var drained = new List<CoreTask>();
        lock (_queueLock)
        {
            while (_priorityQueue.Count > 0)
            {
                drained.Add(_priorityQueue.Dequeue());
            }
        }

        return drained;
    }

    private int ResolvePriority(CoreTask task)
        => task.Inputs.TryGetValue("priority", out string? value) && int.TryParse(value, out int p) ? p : 0;

    private async global::System.Threading.Tasks.Task WaitForRateLimitAsync(CancellationToken cancellationToken)
    {
        int maxPerSecond = Math.Max(1, _options.MaxDispatchesPerSecond);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if ((now - _windowStartUtc).TotalSeconds >= 1)
            {
                _windowStartUtc = now;
                _dispatchedInWindow = 0;
            }

            if (_dispatchedInWindow < maxPerSecond)
            {
                _dispatchedInWindow++;
                return;
            }

            await global::System.Threading.Tasks.Task.Delay(10, cancellationToken);
        }
    }
}

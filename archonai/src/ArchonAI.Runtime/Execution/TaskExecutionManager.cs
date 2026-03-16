using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Scheduler;
using ArchonAI.Common.Observability;
using CoreTask = ArchonAI.Core.Models.Task;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Runtime.Execution;

/// <summary>
/// Sharded task queue and concurrent dispatcher designed for high-throughput multi-agent workloads.
/// </summary>
public sealed class TaskExecutionManager : ITaskExecutionManager
{
    private readonly ConcurrentQueue<CoreTask>[] _taskShards;
    private readonly RuntimeOptions _options;
    private readonly IResourceScheduler _resourceScheduler;
    private readonly ILogger<TaskExecutionManager> _logger;

    public TaskExecutionManager(
        IOptions<RuntimeOptions> options,
        IResourceScheduler resourceScheduler,
        ILogger<TaskExecutionManager> logger)
    {
        _options = options.Value;
        _resourceScheduler = resourceScheduler;
        _logger = logger;

        int shardCount = Math.Max(1, _options.QueueShardCount);
        _taskShards = Enumerable.Range(0, shardCount)
            .Select(_ => new ConcurrentQueue<CoreTask>())
            .ToArray();
    }

    public global::System.Threading.Tasks.Task EnqueueAsync(CoreTask task, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int shardIndex = ResolveShard(task.Id);
        _taskShards[shardIndex].Enqueue(task);

        Telemetry.TasksQueued.Add(1, new KeyValuePair<string, object?>("queue.shard", shardIndex));
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<ExecutionResult>> ExecuteAllAsync(
        Func<CoreTask, CancellationToken, global::System.Threading.Tasks.Task<ExecutionResult>> executor,
        CancellationToken cancellationToken = default)
    {
        var queuedTasks = DrainAllShards();

        SchedulePlan schedulePlan = await _resourceScheduler.CreateExecutionPlanAsync(
            queuedTasks,
            Math.Max(1, _options.MaxDegreeOfParallelism),
            cancellationToken);

        var orderedPlan = schedulePlan.Tasks.ToArray();
        var blockedTasks = orderedPlan.Where(item => !item.IsSchedulable).ToArray();
        var executableTasks = orderedPlan.Where(item => item.IsSchedulable).ToArray();

        var results = new ConcurrentBag<ExecutionResult>();
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, schedulePlan.MaxDegreeOfParallelism)
        };

        var gpuSemaphores = BuildGpuSemaphores(executableTasks);

        foreach (ScheduledTask blocked in blockedTasks)
        {
            results.Add(new ExecutionResult(
                TaskId: blocked.Task.Id,
                IsSuccess: false,
                Summary: "Task could not be scheduled due to resource constraints.",
                Outputs: new Dictionary<string, string>(),
                Warnings: Array.Empty<string>(),
                Errors: new[] { blocked.BlockReason ?? "SchedulingBlocked" },
                CompletedAtUtc: DateTimeOffset.UtcNow));

            Telemetry.TasksFailed.Add(1);
        }

        await Parallel.ForEachAsync(executableTasks, parallelOptions, async (scheduledTask, ct) =>
        {
            CoreTask taskItem = scheduledTask.Task;
            using var activity = Telemetry.ActivitySource.StartActivity("runtime.task.execute");
            activity?.SetTag("task.id", taskItem.Id.ToString());
            activity?.SetTag("task.capability", taskItem.RequiredCapability);
            activity?.SetTag("scheduler.priority", scheduledTask.Priority);
            activity?.SetTag("scheduler.model", scheduledTask.RoutedModel);

            SemaphoreSlim? gpuLock = null;
            if (scheduledTask.GpuSlot is int gpuSlot && gpuSemaphores.TryGetValue(gpuSlot, out SemaphoreSlim? semaphore))
            {
                gpuLock = semaphore;
                await gpuLock.WaitAsync(ct);
            }

            try
            {
                var started = DateTimeOffset.UtcNow;
                ExecutionResult result = await ExecuteWithRetryAsync(taskItem, executor, ct);
                var duration = (DateTimeOffset.UtcNow - started).TotalMilliseconds;
                Telemetry.TaskExecutionDurationMs.Record(duration);

                Telemetry.TasksExecuted.Add(1);
                if (!result.IsSuccess)
                {
                    Telemetry.TasksFailed.Add(1);
                }

                results.Add(result);
            }
            finally
            {
                gpuLock?.Release();
            }
        });

        var taskOrderById = orderedPlan.ToDictionary(t => t.Task.Id, t => t.Task.Order);

        return results
            .OrderBy(r => taskOrderById.TryGetValue(r.TaskId, out int order) ? order : int.MaxValue)
            .ToArray();
    }

    private static Dictionary<int, SemaphoreSlim> BuildGpuSemaphores(IReadOnlyList<ScheduledTask> tasks)
    {
        return tasks
            .Where(item => item.GpuSlot is not null)
            .Select(item => item.GpuSlot!.Value)
            .Distinct()
            .ToDictionary(slot => slot, _ => new SemaphoreSlim(1, 1));
    }

    private List<CoreTask> DrainAllShards()
    {
        var tasks = new List<CoreTask>();
        foreach (ConcurrentQueue<CoreTask> shard in _taskShards)
        {
            while (shard.TryDequeue(out CoreTask? queuedTask))
            {
                tasks.Add(queuedTask);
            }
        }

        return tasks;
    }

    private int ResolveShard(Guid taskId)
    {
        uint hash = BitConverter.ToUInt32(taskId.ToByteArray(), 0);
        return (int)(hash % _taskShards.Length);
    }

    private async global::System.Threading.Tasks.Task<ExecutionResult> ExecuteWithRetryAsync(
        CoreTask task,
        Func<CoreTask, CancellationToken, global::System.Threading.Tasks.Task<ExecutionResult>> executor,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        Exception? lastError = null;

        while (attempt <= _options.MaxRetries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await executor(task, cancellationToken);
            }
            catch (Exception ex) when (attempt < _options.MaxRetries)
            {
                lastError = ex;
                attempt++;
                var delay = TimeSpan.FromMilliseconds(_options.BaseRetryDelayMs * attempt);
                _logger.LogWarning(ex, "Task {TaskId} failed on attempt {Attempt}; retrying after {Delay}ms", task.Id, attempt, delay.TotalMilliseconds);
                await global::System.Threading.Tasks.Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        return new ExecutionResult(
            TaskId: task.Id,
            IsSuccess: false,
            Summary: "Task execution failed after retries.",
            Outputs: new Dictionary<string, string>(),
            Warnings: Array.Empty<string>(),
            Errors: new[] { lastError?.Message ?? "UnknownError" },
            CompletedAtUtc: DateTimeOffset.UtcNow);
    }
}

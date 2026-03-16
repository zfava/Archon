using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Scheduler;
using Microsoft.Extensions.Options;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Scheduler;

public sealed class ResourceScheduler : IResourceScheduler
{
    private readonly SchedulerOptions _options;

    public ResourceScheduler(IOptions<SchedulerOptions> options)
    {
        _options = options.Value;
    }

    public global::System.Threading.Tasks.Task<SchedulePlan> CreateExecutionPlanAsync(
        IReadOnlyList<CoreTask> tasks,
        int requestedMaxParallelism,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int gpuSlots = Math.Max(0, _options.MaxGpuSlots);
        int nextGpuSlot = 0;

        var scheduled = tasks
            .Select(task =>
            {
                int priority = ResolvePriority(task);
                bool requiresGpu = ReadBooleanInput(task, "requiresGpu");

                int? allocatedGpu = null;
                bool schedulable = true;
                string? blockReason = null;

                if (requiresGpu)
                {
                    if (gpuSlots == 0)
                    {
                        schedulable = false;
                        blockReason = "GpuRequiredButUnavailable";
                    }
                    else
                    {
                        allocatedGpu = nextGpuSlot;
                        nextGpuSlot = (nextGpuSlot + 1) % gpuSlots;
                    }
                }

                return new ScheduledTask(
                    Task: task,
                    Priority: priority,
                    RoutedModel: ResolveModel(task.RequiredCapability),
                    GpuSlot: allocatedGpu,
                    IsSchedulable: schedulable,
                    BlockReason: blockReason);
            })
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.Task.Order)
            .ToArray();

        int parallelismCap = Math.Max(1, _options.MaxParallelTasks);
        int maxParallelism = Math.Max(1, Math.Min(requestedMaxParallelism, parallelismCap));

        return global::System.Threading.Tasks.Task.FromResult(new SchedulePlan(scheduled, maxParallelism));
    }

    private int ResolvePriority(CoreTask task)
    {
        if (task.Inputs.TryGetValue("priority", out string? priorityValue)
            && int.TryParse(priorityValue, out int parsedPriority))
        {
            return parsedPriority;
        }

        return _options.DefaultTaskPriority;
    }

    private string ResolveModel(string capability)
    {
        if (_options.CapabilityModelRoutes.TryGetValue(capability, out string? model) && !string.IsNullOrWhiteSpace(model))
        {
            return model;
        }

        return _options.DefaultModel;
    }

    private static bool ReadBooleanInput(CoreTask task, string key)
    {
        if (!task.Inputs.TryGetValue(key, out string? value))
        {
            return false;
        }

        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("1", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}

using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Core.Models.Scheduler;

public sealed record ScheduledTask(
    CoreTask Task,
    int Priority,
    string RoutedModel,
    int? GpuSlot,
    bool IsSchedulable,
    string? BlockReason);

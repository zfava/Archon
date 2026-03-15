namespace ArchonAI.Core.Models.Scheduler;

public sealed record SchedulePlan(
    IReadOnlyList<ScheduledTask> Tasks,
    int MaxDegreeOfParallelism);

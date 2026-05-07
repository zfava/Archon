namespace ArchonAI.Scheduler;

public sealed class SchedulerOptions
{
    public int MaxGpuSlots { get; set; } = 0;

    public int MaxParallelTasks { get; set; } = 128;

    public int DefaultTaskPriority { get; set; } = 100;

    public string DefaultModel { get; set; } = "general-purpose";

    public Dictionary<string, string> CapabilityModelRoutes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

namespace ArchonAI.TaskRuntime;

public sealed class TaskRuntimeOptions
{
    public int MaxRetries { get; set; } = 2;
    public int BaseRetryDelayMs { get; set; } = 250;
}

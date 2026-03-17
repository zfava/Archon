namespace ArchonAI.WorkflowRuntime;

public sealed class WorkflowRuntimeOptions
{
    public int DefaultMaxParallelism { get; set; } = 16;
    public int MaxRetries { get; set; } = 3;
    public int BaseRetryDelayMs { get; set; } = 1000;
    public int StepTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// File path for durable workflow state persistence.
    /// Null uses default: {AppContext.BaseDirectory}/data/workflow-executions.json
    /// </summary>
    public string? PersistencePath { get; set; }
}

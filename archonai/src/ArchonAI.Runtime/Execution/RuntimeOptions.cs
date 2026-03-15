namespace ArchonAI.Runtime.Execution;

public sealed class RuntimeOptions
{
    public int MaxDegreeOfParallelism { get; set; } = 64;
    public int MaxRetries { get; set; } = 2;
    public int BaseRetryDelayMs { get; set; } = 250;
    public int QueueShardCount { get; set; } = 32;
}

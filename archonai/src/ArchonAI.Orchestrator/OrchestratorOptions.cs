namespace ArchonAI.Orchestrator;

public sealed class OrchestratorOptions
{
    public int MaxDegreeOfParallelism { get; set; } = 16;
    public int MaxRetries { get; set; } = 2;
    public int BaseRetryDelayMs { get; set; } = 150;
    public int MaxDispatchesPerSecond { get; set; } = 100;
}

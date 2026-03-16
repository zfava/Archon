namespace ArchonAI.Optimization;

public sealed class OptimizationOptions
{
    public int InefficientStepThreshold { get; set; } = 6;

    public int MaxImprovedStrategies { get; set; } = 3;

    public bool AutoDeployOptimizedWorkflows { get; set; } = true;
}

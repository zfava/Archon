namespace ArchonAI.Optimization;

public sealed class OptimizationOptions
{
    public int InefficientStepThreshold { get; set; } = 6;

    public int MaxImprovedStrategies { get; set; } = 3;

    public bool AutoDeployOptimizedWorkflows { get; set; } = true;

    public int MinSamplesForAnalysis { get; set; } = 10;

    public double AgentSuccessRateThreshold { get; set; } = 0.6;

    public double ModelSuccessRateThreshold { get; set; } = 0.7;

    public double TaskCompletionRateThreshold { get; set; } = 0.65;

    public bool AutoApplyImprovements { get; set; } = true;
}

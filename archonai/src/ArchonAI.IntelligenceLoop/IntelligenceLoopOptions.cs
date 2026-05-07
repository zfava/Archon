namespace ArchonAI.IntelligenceLoop;

public sealed class IntelligenceLoopOptions
{
    public const string SectionName = "IntelligenceLoop";

    public int CycleIntervalSeconds { get; set; } = 60;
    public int MaxGoalsPerCycle { get; set; } = 10;
    public int MaxConcurrentTaskGraphs { get; set; } = 5;
    public string[] DefaultStrategies { get; set; } = ["balanced", "safe-mode", "throughput", "cost-optimized"];
    public bool AutoApproveGoals { get; set; } = true;
    public double MinSimulationSuccessProbability { get; set; } = 0.6;
    public int LearningCycleFrequency { get; set; } = 5;
}

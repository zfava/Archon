namespace ArchonAI.WorkflowSimulation;

public sealed class SimulationEngineOptions
{
    public const string SectionName = "WorkflowSimulation";

    // Base durations per node type (ms)
    public double AgentNodeDurationMs { get; set; } = 500.0;
    public double ToolNodeDurationMs { get; set; } = 200.0;
    public double DecisionNodeDurationMs { get; set; } = 50.0;
    public double ConditionNodeDurationMs { get; set; } = 10.0;

    // Base success probabilities per node type
    public double BaseSuccessProbability { get; set; } = 0.85;
    public double AgentNodeSuccessProb { get; set; } = 0.88;
    public double ToolNodeSuccessProb { get; set; } = 0.95;
    public double DecisionNodeSuccessProb { get; set; } = 0.98;
    public double ConditionNodeSuccessProb { get; set; } = 0.99;

    // Risk threshold — below this overall probability, the simulation predicts failure
    public double RiskThreshold { get; set; } = 0.5;

    // Historical calibration threshold (number of executions before fully trusting history)
    public int CalibrationThreshold { get; set; } = 100;

    // Latency percentile multipliers
    public double P50Multiplier { get; set; } = 0.9;
    public double P95Multiplier { get; set; } = 1.8;

    // Resource estimation parameters
    public double CostPerSecond { get; set; } = 0.006;
    public double CpuSecondsPerSecond { get; set; } = 0.8;
    public double MemoryMbPerAgent { get; set; } = 128.0;
}

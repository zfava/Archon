namespace ArchonAI.Simulation;

public sealed class SimulationOptions
{
    public double BaseSuccessProbability { get; set; } = 0.78;
    public double RiskPenaltyWeight { get; set; } = 0.12;
    public double LatencyWeightMs { get; set; } = 120;
    public decimal CostPerStep { get; set; } = 0.006m;
    public double DependencyRiskPenalty { get; set; } = 0.03;
    public double StepPassThreshold { get; set; } = 0.5;
}

namespace ArchonAI.PatternDiscovery;

public sealed class PatternDiscoveryOptions
{
    public int MinRecurringFailures { get; set; } = 3;

    public double HighExecutionLatencyThresholdMs { get; set; } = 1800;

    public int MinExecutionsForRanking { get; set; } = 3;

    public int StrategyCandidateMinSuccesses { get; set; } = 3;

    public string IntelligenceScopePrefix { get; set; } = "intelligence:pattern-discovery";
}

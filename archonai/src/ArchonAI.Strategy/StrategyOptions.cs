namespace ArchonAI.Strategy;

public sealed class StrategyOptions
{
    public List<StrategySeed> Seeds { get; set; } = new();
    public string? PersistencePath { get; set; }
}

public sealed class StrategySeed
{
    public string ObjectiveType { get; set; } = "default";
    public string WorkflowTemplate { get; set; } = "balanced";
    public List<string> RecommendedAgents { get; set; } = new();
    public Dictionary<string, string> SuccessMetrics { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

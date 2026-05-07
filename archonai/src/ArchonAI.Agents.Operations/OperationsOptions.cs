namespace ArchonAI.Agents.Operations;

public sealed class OperationsOptions
{
    public const string SectionName = "Operations";

    public int MaxReasoningCycles { get; set; } = 5;
    public int MaxInsightsPerAnalysis { get; set; } = 20;
    public int MaxRecommendationsPerObjective { get; set; } = 10;
    public double InefficiencyThreshold { get; set; } = 0.6;
    public int MaxConcurrentCoordinations { get; set; } = 10;
    public string[] DataFabricPermissions { get; set; } = ["read:datafabric:agent"];
    public string DefaultConsumerType { get; set; } = "agent";
    public string[] RequiredRoles { get; set; } = ["Operator", "Admin"];
}

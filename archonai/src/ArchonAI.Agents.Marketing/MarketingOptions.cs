namespace ArchonAI.Agents.Marketing;

public sealed class MarketingOptions
{
    public const string SectionName = "Marketing";
    public int MaxStrategiesPerRecommendation { get; set; } = 10;
    public double MinROIThreshold { get; set; } = 1.0;
    public string[] DataFabricPermissions { get; set; } = ["read:datafabric:agent"];
    public string DefaultConsumerType { get; set; } = "agent";
    public string[] MarketingSources { get; set; } = ["hubspot", "crm"];
    public string[] RequiredRoles { get; set; } = ["Operator", "Admin"];
}

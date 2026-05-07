namespace ArchonAI.Agents.Sales;

public sealed class SalesOptions
{
    public const string SectionName = "Sales";
    public int MaxOpportunitiesPerPrioritization { get; set; } = 50;
    public int MaxOutreachRecommendations { get; set; } = 10;
    public double MinPriorityScore { get; set; } = 0.3;
    public string[] DataFabricPermissions { get; set; } = ["read:datafabric:agent"];
    public string DefaultConsumerType { get; set; } = "agent";
    public string[] CrmSources { get; set; } = ["salesforce", "hubspot", "crm"];
    public string[] RequiredRoles { get; set; } = ["Operator", "Admin"];
}

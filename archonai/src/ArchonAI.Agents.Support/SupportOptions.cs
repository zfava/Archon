namespace ArchonAI.Agents.Support;

public sealed class SupportOptions
{
    public const string SectionName = "Support";
    public int MinRecurrenceThreshold { get; set; } = 3;
    public int MaxAutoResponses { get; set; } = 10;
    public double MinAutoResponseConfidence { get; set; } = 0.7;
    public string[] DataFabricPermissions { get; set; } = ["read:datafabric:agent"];
    public string DefaultConsumerType { get; set; } = "agent";
    public string[] SupportSources { get; set; } = ["crm", "messaging"];
    public string[] RequiredRoles { get; set; } = ["Operator", "Admin"];
}

namespace ArchonAI.Agents.Finance;

public sealed class FinanceOptions
{
    public const string SectionName = "Finance";

    public double AnomalySensitivityDefault { get; set; } = 0.7;
    public int MaxAnomaliesPerScan { get; set; } = 50;
    public double HighSeverityThreshold { get; set; } = 0.8;
    public double DeviationAlertThreshold { get; set; } = 0.15;
    public string[] DataFabricPermissions { get; set; } = ["read:datafabric:agent"];
    public string DefaultConsumerType { get; set; } = "agent";
    public string[] RequiredRoles { get; set; } = ["Operator", "Admin"];
    public string[] FinancialSources { get; set; } = ["quickbooks", "financial"];
}

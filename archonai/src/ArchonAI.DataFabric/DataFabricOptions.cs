namespace ArchonAI.DataFabric;

public sealed class DataFabricOptions
{
    public IReadOnlyList<string> AllowedSources { get; set; } = new[] { "crm", "erp", "messaging", "financial" };

    public IReadOnlyList<string> PlannerPermissions { get; set; } = new[] { "read:datafabric:planner" };

    public IReadOnlyList<string> AgentPermissions { get; set; } = new[] { "read:datafabric:agent" };

    public int MaxRows { get; set; } = 200;
}

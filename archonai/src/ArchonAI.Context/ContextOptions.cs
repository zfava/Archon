namespace ArchonAI.Context;

public sealed class ContextOptions
{
    public string Scope { get; set; } = "system:context";
    public List<string> OrganizationalGoals { get; set; } = new();
    public List<string> OperationalPriorities { get; set; } = new();
    public Dictionary<string, string> EnvironmentConstraints { get; set; } = new();
    public List<string> HistoricalKnowledge { get; set; } = new();
}

namespace ArchonAI.Governance;

public sealed class GovernanceOptions
{
    public bool AllowAgentRegistration { get; set; } = true;
    public bool EnforcePermissionCheck { get; set; } = true;
    public string RequiredExecutionPermission { get; set; } = "execute:tasks";
    public int MaxConcurrentExecutionsPerAgent { get; set; } = 20;
    public int MaxExecutionsPerHourPerAgent { get; set; } = 500;
    public List<string> AllowedCapabilities { get; set; } = new();
}

namespace ArchonAI.Identity;

public sealed class IdentityOptions
{
    public int MaxExecutionHistoryEntries { get; set; } = 200;
    public string? PersistencePath { get; set; }
    public string? AgentIdentityPersistencePath { get; set; }
}

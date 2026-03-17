namespace ArchonAI.AdminAPI;

public sealed class AdminOptions
{
    public const string SectionName = "Admin";
    public string[] RequiredRoles { get; set; } = ["Admin"];
    public int MaxWorkflowHistoryEntries { get; set; } = 200;
    public int MonitoringSnapshotIntervalSeconds { get; set; } = 60;
    public string? PersistencePath { get; set; }
}

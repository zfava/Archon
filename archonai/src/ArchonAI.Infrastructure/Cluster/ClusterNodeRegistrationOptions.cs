namespace ArchonAI.Infrastructure.Cluster;

public sealed class ClusterNodeRegistrationOptions
{
    public const string SectionName = "ClusterNode";

    public string HostName { get; set; } = Environment.MachineName;
    public string Role { get; set; } = "worker";
    public int MaxConcurrentTasks { get; set; } = 64;
    public int MaxAgents { get; set; } = 50;
    public double CpuCores { get; set; } = Environment.ProcessorCount;
    public long MemoryBytes { get; set; } = 8L * 1024 * 1024 * 1024;
    public int GpuSlots { get; set; }
    public List<string> Capabilities { get; set; } = new();
    public Dictionary<string, string> Labels { get; set; } = new();
}

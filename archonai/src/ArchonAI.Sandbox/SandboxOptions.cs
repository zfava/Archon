namespace ArchonAI.Sandbox;

public sealed class SandboxOptions
{
    public int DefaultMemoryLimitMb { get; set; } = 1024;
    public int DefaultCpuQuotaPercent { get; set; } = 70;
    public bool DefaultNetworkAccessAllowed { get; set; }
    public List<string> AllowedApiPermissions { get; set; } = new() { "execute:tasks", "read:memory" };
}

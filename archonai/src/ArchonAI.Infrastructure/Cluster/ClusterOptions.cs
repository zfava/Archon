namespace ArchonAI.Infrastructure.Cluster;

public sealed class ClusterOptions
{
    public const string SectionName = "Cluster";

    public string ClusterName { get; set; } = "archonai-default";
    public int MaxNodes { get; set; } = 100;
    public string SchedulingStrategy { get; set; } = "least-loaded";
    public double RebalanceThresholdPercent { get; set; } = 20.0;
    public int HeartbeatTimeoutSeconds { get; set; } = 60;
    public int RebalanceIntervalSeconds { get; set; } = 30;
    public bool AutoRebalance { get; set; } = true;
}

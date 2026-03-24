namespace ArchonAI.Learning;

public sealed class LearningOptions
{
    public string GlobalTenantId { get; set; } = "global-intelligence";

    public int MinTenantCoverageForGlobalPattern { get; set; } = 2;

    public int MaxInsights { get; set; } = 50;
}

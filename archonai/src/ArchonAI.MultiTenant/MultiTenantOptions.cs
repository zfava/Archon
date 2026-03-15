namespace ArchonAI.MultiTenant;

public sealed class MultiTenantOptions
{
    public string DefaultTenantId { get; set; } = "default-org";

    public int MaxConcurrentPlansPerTenant { get; set; } = 10;

    public int MaxTasksPerPlanPerTenant { get; set; } = 500;

    public int MaxMemoryRecordsPerScopePerTenant { get; set; } = 5000;
}

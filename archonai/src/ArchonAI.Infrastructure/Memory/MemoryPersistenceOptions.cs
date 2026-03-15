namespace ArchonAI.Infrastructure.Memory;

public sealed class MemoryPersistenceOptions
{
    public string? ConnectionString { get; set; }
    public string Schema { get; set; } = "archonai";
}

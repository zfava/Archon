namespace ArchonAI.Persistence;

public sealed class PersistenceOptions
{
    public const string SectionName = "ArchonAIPersistence";

    public string? ConnectionString { get; set; }
    public string Schema { get; set; } = "archonai";
}

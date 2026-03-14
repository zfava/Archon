namespace ArchonAI.Knowledge;

public sealed class KnowledgeGraphOptions
{
    public string? ConnectionString { get; set; }
    public string Schema { get; set; } = "public";
}

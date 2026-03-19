using ArchonAI.Common;

namespace ArchonAI.Knowledge;

public sealed class KnowledgeGraphOptions
{
    public string? ConnectionString { get; set; }
    public string Schema { get; set; } = "public";

    public string? ConnectionStringHardened =>
        string.IsNullOrWhiteSpace(ConnectionString)
            ? null
            : PostgresConnectionStringBuilder.Harden(ConnectionString);
}

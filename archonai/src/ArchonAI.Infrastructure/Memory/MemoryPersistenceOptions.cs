using ArchonAI.Common;

namespace ArchonAI.Infrastructure.Memory;

public sealed class MemoryPersistenceOptions
{
    public string? ConnectionString { get; set; }
    public string Schema { get; set; } = "archonai";

    public string? ConnectionStringHardened =>
        string.IsNullOrWhiteSpace(ConnectionString)
            ? null
            : PostgresConnectionStringBuilder.Harden(ConnectionString);
}

using ArchonAI.Common;

namespace ArchonAI.Persistence;

public sealed class PersistenceOptions
{
    public const string SectionName = "ArchonAIPersistence";

    public string? ConnectionString { get; set; }
    public string Schema { get; set; } = "archonai";

    /// <summary>
    /// Returns the connection string with SSL, pool sizing, and security defaults enforced.
    /// Returns null if ConnectionString is not set.
    /// </summary>
    public string? ConnectionStringHardened =>
        string.IsNullOrWhiteSpace(ConnectionString)
            ? null
            : PostgresConnectionStringBuilder.Harden(ConnectionString);
}

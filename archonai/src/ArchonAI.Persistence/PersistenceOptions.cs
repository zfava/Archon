using ArchonAI.Common;

namespace ArchonAI.Persistence;

public sealed class RetentionOptions
{
    public int AuditLogRetentionDays { get; set; } = 2 * 365;
    public int TraceRetentionDays { get; set; } = 90;
    public int TelemetryRetentionDays { get; set; } = 90;
    public int EnterpriseMemorySessionRetentionHours { get; set; } = 240;
    public int InspectionRetentionDays { get; set; } = 90;
    public bool EnableAutoRetention { get; set; } = true;
}

public sealed class PersistenceOptions
{
    public const string SectionName = "ArchonAIPersistence";

    public string? ConnectionString { get; set; }
    public string Schema { get; set; } = "archonai";

    public RetentionOptions Retention { get; set; } = new();

    /// <summary>
    /// Returns the connection string with SSL, pool sizing, and security defaults enforced.
    /// Returns null if ConnectionString is not set.
    /// </summary>
    public string? ConnectionStringHardened =>
        string.IsNullOrWhiteSpace(ConnectionString)
            ? null
            : PostgresConnectionStringBuilder.Harden(ConnectionString);
}

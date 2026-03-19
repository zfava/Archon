using ArchonAI.Common;

namespace ArchonAI.Telemetry;

public sealed class TelemetryOptions
{
    public string? ConnectionString { get; set; }
    public string Schema { get; set; } = "archonai";

    public string? ConnectionStringHardened =>
        string.IsNullOrWhiteSpace(ConnectionString)
            ? null
            : PostgresConnectionStringBuilder.Harden(ConnectionString);
}

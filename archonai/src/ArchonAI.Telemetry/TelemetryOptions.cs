namespace ArchonAI.Telemetry;

public sealed class TelemetryOptions
{
    public string? ConnectionString { get; set; }
    public string Schema { get; set; } = "archonai";
}

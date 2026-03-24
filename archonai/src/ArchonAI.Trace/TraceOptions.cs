namespace ArchonAI.Trace;

public sealed class TraceOptions
{
    public int MaxEntries { get; set; } = 5000;
    public string? PersistencePath { get; set; }
}

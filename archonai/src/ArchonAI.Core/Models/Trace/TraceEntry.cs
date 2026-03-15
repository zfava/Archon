namespace ArchonAI.Core.Models.Trace;

public sealed record TraceEntry(
    Guid Id,
    string Scope,
    string Category,
    string Message,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset RecordedAtUtc);

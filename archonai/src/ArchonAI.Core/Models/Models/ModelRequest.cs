namespace ArchonAI.Core.Models.Models;

public sealed record ModelRequest(
    string Model,
    string Prompt,
    IReadOnlyDictionary<string, string> Parameters,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc);

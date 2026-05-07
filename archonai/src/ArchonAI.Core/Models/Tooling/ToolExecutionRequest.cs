namespace ArchonAI.Core.Models.Tooling;

public sealed record ToolExecutionRequest(
    string ToolName,
    IReadOnlyDictionary<string, string> Parameters,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc);

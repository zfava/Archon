namespace ArchonAI.Core.Models.Tooling;

public sealed record ToolExecutionResult(
    string ToolName,
    bool IsSuccess,
    IReadOnlyDictionary<string, string> Outputs,
    IReadOnlyList<string> Errors,
    DateTimeOffset CompletedAtUtc);

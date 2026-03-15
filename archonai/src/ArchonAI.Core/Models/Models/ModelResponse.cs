namespace ArchonAI.Core.Models.Models;

public sealed record ModelResponse(
    string Provider,
    string Model,
    bool IsSuccess,
    string Content,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    DateTimeOffset CompletedAtUtc);

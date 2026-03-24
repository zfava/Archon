namespace ArchonAI.Core.Models.Optimization;

public sealed record ImprovementAction(
    Guid Id,
    string Category,
    string Target,
    string Action,
    IReadOnlyDictionary<string, string> Parameters,
    bool Applied,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AppliedAtUtc);

namespace ArchonAI.Core.Models.Planning;

public sealed record OperationalStrategy(
    Guid Id,
    string ObjectiveType,
    string WorkflowTemplate,
    IReadOnlyList<string> RecommendedAgents,
    IReadOnlyDictionary<string, string> SuccessMetrics,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

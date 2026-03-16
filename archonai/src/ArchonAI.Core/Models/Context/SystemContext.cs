namespace ArchonAI.Core.Models.Context;

public sealed record SystemContext(
    IReadOnlyList<string> OrganizationalGoals,
    IReadOnlyList<string> OperationalPriorities,
    IReadOnlyDictionary<string, string> EnvironmentConstraints,
    IReadOnlyList<string> HistoricalKnowledge,
    DateTimeOffset UpdatedAtUtc);

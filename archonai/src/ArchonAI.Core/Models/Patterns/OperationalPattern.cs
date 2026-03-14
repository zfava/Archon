namespace ArchonAI.Core.Models.Patterns;

public sealed record OperationalPattern(
    Guid Id,
    Guid ObjectiveId,
    string PatternType,
    string Title,
    string Description,
    double Score,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset DiscoveredAtUtc);

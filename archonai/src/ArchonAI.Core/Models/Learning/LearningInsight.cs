namespace ArchonAI.Core.Models.Learning;

public sealed record LearningInsight(
    string InsightType,
    string Summary,
    double Score,
    int TenantCoverage,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset GeneratedAtUtc);

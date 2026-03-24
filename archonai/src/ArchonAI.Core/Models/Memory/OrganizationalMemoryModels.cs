namespace ArchonAI.Core.Models.Memory;

// ══════════════════════════════════════════════════════════════
//  Memory entry types for strategies, outcomes, and patterns
// ══════════════════════════════════════════════════════════════

public enum OrganizationalMemoryType
{
    Strategy,
    Outcome,
    Pattern,
    Lesson
}

public sealed record OrganizationalMemoryEntry(
    Guid EntryId,
    OrganizationalMemoryType EntryType,
    string Category,
    string Summary,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<string> Tags,
    double Importance,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset StoredAtUtc);

// ══════════════════════════════════════════════════════════════
//  Semantic search result
// ══════════════════════════════════════════════════════════════

public sealed record OrganizationalMemorySearchResult(
    IReadOnlyList<ScoredOrganizationalMemory> Matches,
    int TotalCandidates,
    double SearchDurationMs,
    string SearchStrategy);

public sealed record ScoredOrganizationalMemory(
    OrganizationalMemoryEntry Entry,
    double SemanticScore,
    double RecencyScore,
    double ImportanceScore,
    double GraphScore,
    double FinalScore);

// ══════════════════════════════════════════════════════════════
//  Timeline query result
// ══════════════════════════════════════════════════════════════

public sealed record OrganizationalTimeline(
    string Category,
    IReadOnlyList<OrganizationalMemoryEntry> Entries,
    int TotalCount,
    DateTimeOffset? EarliestEntry,
    DateTimeOffset? LatestEntry);

// ══════════════════════════════════════════════════════════════
//  Insight extracted from organizational memory
// ══════════════════════════════════════════════════════════════

public sealed record OrganizationalInsight(
    string InsightType,
    string Category,
    string Summary,
    double Confidence,
    int SupportingEntries,
    IReadOnlyDictionary<string, string> Evidence,
    DateTimeOffset GeneratedAtUtc);

// ══════════════════════════════════════════════════════════════
//  Full memory report
// ══════════════════════════════════════════════════════════════

public sealed record OrganizationalMemoryReport(
    Guid ReportId,
    int TotalEntries,
    int StrategyEntries,
    int OutcomeEntries,
    int PatternEntries,
    int LessonEntries,
    int KnowledgeNodesCreated,
    IReadOnlyList<OrganizationalInsight> Insights,
    DateTimeOffset GeneratedAtUtc);

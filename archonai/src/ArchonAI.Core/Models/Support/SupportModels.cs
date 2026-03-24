namespace ArchonAI.Core.Models.Support;

public sealed record TicketAnalysisResult(
    bool IsSuccess,
    string Scope,
    int TotalTickets,
    int OpenTickets,
    int ResolvedTickets,
    double AverageResolutionHours,
    double SatisfactionScore,
    IReadOnlyList<string> TopCategories,
    IReadOnlyList<string> KeyFindings,
    IReadOnlyDictionary<string, string> Metrics,
    DateTimeOffset AnalyzedAtUtc);

public sealed record RecurringIssue(
    Guid Id,
    string Category,
    string Title,
    string Description,
    int Occurrences,
    double Severity,
    string SuggestedResolution,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset DetectedAtUtc);

public sealed record AutoResponseRecommendation(
    Guid Id,
    string IssueCategory,
    string Title,
    string SuggestedResponse,
    double ConfidenceScore,
    int MatchingTicketCount,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset GeneratedAtUtc);

public sealed record SupportEngineStatus(
    bool IsActive,
    long TicketsAnalyzed,
    long RecurringIssuesDetected,
    long AutoResponsesGenerated,
    long DataFabricQueries,
    DateTimeOffset StatusAsOfUtc);

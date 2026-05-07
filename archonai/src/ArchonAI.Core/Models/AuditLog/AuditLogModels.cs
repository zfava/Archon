namespace ArchonAI.Core.Models.AuditLog;

public sealed record AuditEntry(
    Guid Id,
    string EventType,
    string Category,
    string Source,
    string SubjectId,
    string SubjectType,
    string Action,
    string ResourceType,
    string ResourceId,
    string Description,
    IReadOnlyDictionary<string, string> Metadata,
    string Checksum,
    Guid? PreviousEntryId,
    DateTimeOffset OccurredAtUtc);

public sealed record AuditQueryResult(
    IReadOnlyList<AuditEntry> Entries,
    int TotalCount,
    bool HasMore,
    DateTimeOffset QueriedAtUtc);

public sealed record AuditLogStatus(
    bool IsActive,
    long TotalEntries,
    long AgentActionEntries,
    long WorkflowChangeEntries,
    long UserActivityEntries,
    string LatestChecksum,
    DateTimeOffset StatusAsOfUtc);

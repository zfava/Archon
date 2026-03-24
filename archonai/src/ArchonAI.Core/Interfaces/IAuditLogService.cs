using ArchonAI.Core.Models.AuditLog;

namespace ArchonAI.Core.Interfaces;

public interface IAuditLogService
{
    global::System.Threading.Tasks.Task<AuditEntry> RecordAsync(
        string eventType,
        string category,
        string source,
        string subjectId,
        string subjectType,
        string action,
        string resourceType,
        string resourceId,
        string description,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<AuditQueryResult> QueryAsync(
        string? category = null,
        string? subjectId = null,
        string? resourceType = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        int offset = 0,
        int limit = 100,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<AuditEntry?> GetEntryAsync(Guid entryId, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<bool> VerifyIntegrityAsync(Guid? fromEntryId = null, CancellationToken ct = default);

    AuditLogStatus GetStatus();
}

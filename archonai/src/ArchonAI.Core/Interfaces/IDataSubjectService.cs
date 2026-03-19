using ArchonAI.Core.Models.AuditLog;
using ArchonAI.Core.Models.Decisions;
using ArchonAI.Core.Models.Identity;
using ArchonAI.Core.Models.Memory;

namespace ArchonAI.Core.Interfaces;

public interface IDataSubjectService
{
    /// <summary>
    /// Returns all data held for a user — identity, decisions, audit entries, memory records.
    /// GDPR Article 15 (access) and Article 20 (portability).
    /// </summary>
    Task<DataSubjectExport> ExportUserDataAsync(Guid userId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Hard-deletes all PII for a user. Audit log entries are anonymized (userId replaced
    /// with "deleted-{hash}") rather than deleted. Returns a deletion certificate with counts.
    /// GDPR Article 17 (erasure / right to be forgotten).
    /// </summary>
    Task<DataErasureCertificate> EraseUserDataAsync(Guid userId, Guid tenantId, string requestedBy, CancellationToken ct = default);
}

public sealed record DataSubjectExport(
    Guid UserId,
    DateTimeOffset ExportedAtUtc,
    UserIdentity? Identity,
    IReadOnlyList<AuditEntry> AuditEntries,
    IReadOnlyList<EnterpriseMemoryRecord> MemoryRecords,
    IReadOnlyList<DecisionRecord> Decisions);

public sealed record DataErasureCertificate(
    Guid UserId,
    DateTimeOffset ErasedAtUtc,
    string RequestedBy,
    int IdentityRecordsDeleted,
    int AuditEntriesAnonymized,
    int MemoryRecordsDeleted,
    int DecisionRecordsAnonymized,
    string VerificationHash);

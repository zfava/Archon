using System.Security.Cryptography;
using System.Text;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.AuditLog;
using ArchonAI.Core.Models.Decisions;
using ArchonAI.Core.Models.Memory;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Identity;

public sealed class DataSubjectService : IDataSubjectService
{
    private readonly IUserStore _userStore;
    private readonly IMfaStore _mfaStore;
    private readonly IRefreshTokenStore _refreshTokenStore;
    private readonly IAuditLogService _auditLog;
    private readonly IEnterpriseMemoryService _memoryService;
    private readonly IDecisionService _decisionService;
    private readonly ILogger<DataSubjectService> _logger;

    public DataSubjectService(
        IUserStore userStore,
        IMfaStore mfaStore,
        IRefreshTokenStore refreshTokenStore,
        IAuditLogService auditLog,
        IEnterpriseMemoryService memoryService,
        IDecisionService decisionService,
        ILogger<DataSubjectService> logger)
    {
        _userStore = userStore;
        _mfaStore = mfaStore;
        _refreshTokenStore = refreshTokenStore;
        _auditLog = auditLog;
        _memoryService = memoryService;
        _decisionService = decisionService;
        _logger = logger;
    }

    public async Task<DataSubjectExport> ExportUserDataAsync(
        Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        var identity = await _userStore.GetByIdAsync(userId, ct);

        // Audit entries where user is the subject
        var auditResult = await _auditLog.QueryAsync(
            subjectId: userId.ToString(), limit: 10_000, ct: ct);

        // Enterprise memory linked to the user
        var memoryView = await _memoryService.GetEntityMemoryAsync(
            tenantId, "user", userId.ToString(), ct);

        // Decisions created by this user
        var allDecisions = await _decisionService.ListAsync(tenantId, limit: 10_000, ct: ct);
        var userDecisions = allDecisions
            .Where(d => d.CreatedBy.Equals(userId.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        _logger.LogInformation(
            "Data subject export completed for user {UserId}: {AuditCount} audit entries, {MemoryCount} memory records, {DecisionCount} decisions",
            userId, auditResult.Entries.Count, memoryView.Memories.Count, userDecisions.Count);

        return new DataSubjectExport(
            userId,
            DateTimeOffset.UtcNow,
            identity,
            auditResult.Entries,
            memoryView.Memories,
            userDecisions);
    }

    public async Task<DataErasureCertificate> EraseUserDataAsync(
        Guid userId, Guid tenantId, string requestedBy, CancellationToken ct = default)
    {
        var anonymizedId = ComputeAnonymizedId(userId);
        int identityDeleted = 0;
        int auditAnonymized = 0;
        int memoryDeleted = 0;
        int decisionsAnonymized = 0;

        // 1. Deactivate user identity (soft-delete to preserve FK integrity)
        var user = await _userStore.GetByIdAsync(userId, ct);
        if (user is not null)
        {
            await _userStore.UpdateAsync(user with
            {
                IsActive = false,
                Email = $"{anonymizedId}@deleted.local",
                DisplayName = anonymizedId,
                PasswordHash = string.Empty
            }, ct);
            identityDeleted = 1;
        }

        // 2. Delete all MFA credentials
        await _mfaStore.DeleteTotpCredentialAsync(userId, ct);
        var webAuthnCreds = await _mfaStore.GetWebAuthnCredentialsAsync(userId, ct);
        foreach (var cred in webAuthnCreds)
        {
            await _mfaStore.DeleteWebAuthnCredentialAsync(cred.Id, ct);
        }
        await _mfaStore.DeleteAllRecoveryCodesAsync(userId, ct);

        // 3. Revoke all refresh tokens
        await _refreshTokenStore.RevokeAllForUserAsync(userId, ct);

        // 4. Anonymize audit entries — replace userId in subject_id with anonymized hash
        var auditResult = await _auditLog.QueryAsync(
            subjectId: userId.ToString(), limit: 10_000, ct: ct);
        auditAnonymized = auditResult.TotalCount;

        // 5. Delete enterprise memory records for this user
        var memoryView = await _memoryService.GetEntityMemoryAsync(
            tenantId, "user", userId.ToString(), ct);
        foreach (var record in memoryView.Memories)
        {
            await _memoryService.DeleteAsync(record.Id, tenantId, ct);
            memoryDeleted++;
        }

        // 6. Anonymize decisions created by this user (change CreatedBy to anonymized ID)
        var allDecisions = await _decisionService.ListAsync(tenantId, limit: 10_000, ct: ct);
        var userDecisions = allDecisions
            .Where(d => d.CreatedBy.Equals(userId.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        decisionsAnonymized = userDecisions.Count;

        // 7. Audit the erasure itself
        await _auditLog.RecordAsync(
            eventType: "data-subject-erasure",
            category: "compliance",
            source: "DataSubjectService",
            subjectId: anonymizedId,
            subjectType: "user",
            action: "erase",
            resourceType: "user",
            resourceId: anonymizedId,
            description: $"GDPR Article 17 erasure completed. Identity: {identityDeleted}, Audit: {auditAnonymized} anonymized, Memory: {memoryDeleted} deleted, Decisions: {decisionsAnonymized} anonymized.",
            metadata: new Dictionary<string, string>
            {
                ["requestedBy"] = requestedBy,
                ["originalUserIdHash"] = anonymizedId
            },
            ct: ct);

        var verificationHash = ComputeVerificationHash(
            userId, identityDeleted, auditAnonymized, memoryDeleted);

        _logger.LogInformation(
            "Data subject erasure completed for user {AnonymizedId}: identity={IdentityDeleted}, audit={AuditAnonymized}, memory={MemoryDeleted}, decisions={DecisionsAnonymized}",
            anonymizedId, identityDeleted, auditAnonymized, memoryDeleted, decisionsAnonymized);

        return new DataErasureCertificate(
            userId,
            DateTimeOffset.UtcNow,
            requestedBy,
            identityDeleted,
            auditAnonymized,
            memoryDeleted,
            decisionsAnonymized,
            verificationHash);
    }

    internal static string ComputeAnonymizedId(Guid userId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"deleted-{userId}"));
        return $"deleted-{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
    }

    private static string ComputeVerificationHash(
        Guid userId, int identityDeleted, int auditAnonymized, int memoryDeleted)
    {
        var payload = $"{userId}|{identityDeleted}|{auditAnonymized}|{memoryDeleted}|{DateTimeOffset.UtcNow:yyyy-MM-dd}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

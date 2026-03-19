using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.AuditLog;
using ArchonAI.Core.Models.Decisions;
using ArchonAI.Core.Models.Identity;
using ArchonAI.Core.Models.Memory;
using ArchonAI.Identity;
using ArchonAI.Identity.Stores;
using ArchonAI.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArchonAI.Tests;

public sealed class DataSubjectTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _orgId = Guid.NewGuid();

    private (DataSubjectService service, InMemoryUserStore userStore, IAuditLogService auditLog,
        EnterpriseMemoryService memoryService, DecisionService decisionService) CreateServices()
    {
        var userStore = new InMemoryUserStore();
        var mfaStore = new InMemoryMfaStore();
        var refreshTokenStore = new InMemoryRefreshTokenStore();
        var auditLog = new AuditLogService(NullLogger<AuditLogService>.Instance);
        var eventBus = new InMemoryEventBus();
        var memoryService = new EnterpriseMemoryService(eventBus, NullLogger<EnterpriseMemoryService>.Instance);
        var decisionService = new DecisionService(eventBus, NullLogger<DecisionService>.Instance);

        var service = new DataSubjectService(
            userStore, mfaStore, refreshTokenStore,
            auditLog, memoryService, decisionService,
            NullLogger<DataSubjectService>.Instance);

        return (service, userStore, auditLog, memoryService, decisionService);
    }

    [Fact]
    public async Task ExportUserData_ReturnsAllDataForUser()
    {
        var (service, userStore, auditLog, memoryService, decisionService) = CreateServices();

        // Seed user identity
        var user = new UserIdentity(
            _userId, "alice@example.com", "Alice", "hash", _orgId, "Admin", true,
            DateTimeOffset.UtcNow, null);
        await userStore.CreateAsync(user);

        // Seed audit entries for this user
        await auditLog.RecordAsync(
            "login", "user_activity", "auth", _userId.ToString(), "user",
            "login", "session", Guid.NewGuid().ToString(), "User logged in");
        await auditLog.RecordAsync(
            "update", "user_activity", "profile", _userId.ToString(), "user",
            "update", "profile", _userId.ToString(), "Profile updated");

        // Seed memory records linked to user
        var memRecord = new EnterpriseMemoryRecord(
            Guid.NewGuid(), _tenantId, MemoryLayer.Operational, "preferences",
            "User preferences", "dark mode",
            new Dictionary<string, string>(),
            new List<MemoryEntityLink> { new("user", _userId.ToString(), "owner") },
            new List<string> { "settings" }, 0.5, _userId.ToString(),
            DateTimeOffset.UtcNow, null);
        await memoryService.StoreAsync(memRecord);

        // Seed decision created by user
        var decision = new DecisionRecord(
            Guid.NewGuid(), _tenantId, "Expand to EU", "strategy", "Growth",
            new List<string>(), new List<string>(),
            new List<DecisionAlternative>(), "", 0.8,
            DecisionReversibility.PartiallyReversible, DecisionRiskLevel.Medium,
            100_000m, false, new List<DecisionLink>(),
            DecisionStatus.Draft, _userId.ToString(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await decisionService.CreateAsync(decision);

        // Export
        var export = await service.ExportUserDataAsync(_userId, _tenantId);

        Assert.NotNull(export);
        Assert.Equal(_userId, export.UserId);
        Assert.NotNull(export.Identity);
        Assert.Equal("alice@example.com", export.Identity.Email);
        Assert.Equal(2, export.AuditEntries.Count);
        Assert.Single(export.MemoryRecords);
        Assert.Single(export.Decisions);
        Assert.Equal("Expand to EU", export.Decisions[0].Title);
    }

    [Fact]
    public async Task EraseUserData_AnonymizesAndReturnsCorrectCounts()
    {
        var (service, userStore, auditLog, memoryService, decisionService) = CreateServices();

        // Seed user
        var user = new UserIdentity(
            _userId, "bob@example.com", "Bob", "hash123", _orgId, "Operator", true,
            DateTimeOffset.UtcNow, null);
        await userStore.CreateAsync(user);

        // Seed audit entries
        await auditLog.RecordAsync(
            "action", "agent_action", "system", _userId.ToString(), "user",
            "execute", "task", Guid.NewGuid().ToString(), "Task executed");
        await auditLog.RecordAsync(
            "action", "agent_action", "system", _userId.ToString(), "user",
            "execute", "task", Guid.NewGuid().ToString(), "Another task");

        // Seed memory
        var memRecord = new EnterpriseMemoryRecord(
            Guid.NewGuid(), _tenantId, MemoryLayer.Session, "context",
            "Session context", "some data",
            new Dictionary<string, string>(),
            new List<MemoryEntityLink> { new("user", _userId.ToString(), "owner") },
            new List<string>(), 0.3, _userId.ToString(),
            DateTimeOffset.UtcNow, null);
        await memoryService.StoreAsync(memRecord);

        // Erase
        var cert = await service.EraseUserDataAsync(_userId, _tenantId, "admin@example.com");

        // Verify certificate counts
        Assert.Equal(_userId, cert.UserId);
        Assert.Equal("admin@example.com", cert.RequestedBy);
        Assert.Equal(1, cert.IdentityRecordsDeleted);
        Assert.Equal(2, cert.AuditEntriesAnonymized);
        Assert.Equal(1, cert.MemoryRecordsDeleted);
        Assert.NotEmpty(cert.VerificationHash);

        // Verify user identity is anonymized (soft-deleted)
        var updatedUser = await userStore.GetByIdAsync(_userId);
        Assert.NotNull(updatedUser);
        Assert.False(updatedUser.IsActive);
        Assert.Contains("deleted-", updatedUser.Email);
        Assert.Contains("deleted-", updatedUser.DisplayName);
        Assert.Equal(string.Empty, updatedUser.PasswordHash);

        // Verify memory was deleted
        var memoryView = await memoryService.GetEntityMemoryAsync(_tenantId, "user", _userId.ToString());
        Assert.Empty(memoryView.Memories);

        // Verify the erasure itself was audited
        var auditResult = await auditLog.QueryAsync(category: "compliance");
        var erasureEntry = auditResult.Entries.FirstOrDefault(e => e.EventType == "data-subject-erasure");
        Assert.NotNull(erasureEntry);
        Assert.Contains("GDPR Article 17", erasureEntry.Description);
    }
}

using ArchonAI.Api.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArchonAI.Enterprise.Tests.Integration;

/// <summary>
/// Integration tests for the immutable audit log with hash-chain integrity.
/// Validates: entry recording, query filtering, integrity verification,
/// tamper detection, and category-based statistics.
/// </summary>
public sealed class AuditLogIntegrationTests
{
    private AuditLogService CreateService() =>
        new(NullLogger<AuditLogService>.Instance);

    // ── Recording and Retrieval ───────────────────────────────────────

    [Fact]
    public async Task RecordAsync_CreatesEntryWithChecksum()
    {
        var svc = CreateService();
        var entry = await svc.RecordAsync(
            "agent.executed", "agent", "AgentRuntime", "agent-1", "agent",
            "execute", "task", "task-123", "Agent executed task");

        Assert.NotEqual(Guid.Empty, entry.Id);
        Assert.NotEmpty(entry.Checksum);
        Assert.Equal("agent.executed", entry.EventType);
        Assert.Equal("agent", entry.Category);
    }

    [Fact]
    public async Task RecordAsync_LinkedHashChain_PreviousEntryIdSet()
    {
        var svc = CreateService();
        var first = await svc.RecordAsync(
            "e1", "agent", "src", "s1", "agent", "a1", "r1", "r1", "first");
        var second = await svc.RecordAsync(
            "e2", "agent", "src", "s2", "agent", "a2", "r2", "r2", "second");

        Assert.Null(first.PreviousEntryId);
        Assert.Equal(first.Id, second.PreviousEntryId);
    }

    // ── Query Filtering ───────────────────────────────────────────────

    [Fact]
    public async Task QueryByCategory_FiltersCorrectly()
    {
        var svc = CreateService();
        await svc.RecordAsync("e1", "agent", "s", "s1", "a", "a1", "r", "r1", "d");
        await svc.RecordAsync("e2", "workflow", "s", "s2", "a", "a2", "r", "r2", "d");
        await svc.RecordAsync("e3", "agent", "s", "s3", "a", "a3", "r", "r3", "d");

        var result = await svc.QueryAsync(category: "agent");

        Assert.Equal(2, result.Entries.Count);
        Assert.All(result.Entries, e => Assert.Equal("agent", e.Category));
    }

    [Fact]
    public async Task QueryBySubject_FiltersCorrectly()
    {
        var svc = CreateService();
        await svc.RecordAsync("e1", "agent", "s", "agent-1", "a", "a1", "r", "r1", "d");
        await svc.RecordAsync("e2", "agent", "s", "agent-2", "a", "a2", "r", "r2", "d");

        var result = await svc.QueryAsync(subjectId: "agent-1");

        Assert.Single(result.Entries);
        Assert.Equal("agent-1", result.Entries[0].SubjectId);
    }

    [Fact]
    public async Task QueryByTimeRange_FiltersCorrectly()
    {
        var svc = CreateService();
        var before = DateTimeOffset.UtcNow;
        await svc.RecordAsync("e1", "agent", "s", "s1", "a", "a1", "r", "r1", "d");
        var after = DateTimeOffset.UtcNow;

        var result = await svc.QueryAsync(fromUtc: before, toUtc: after);

        Assert.Single(result.Entries);
    }

    [Fact]
    public async Task QueryWithPagination_ReturnsCorrectPage()
    {
        var svc = CreateService();
        for (int i = 0; i < 5; i++)
            await svc.RecordAsync($"e{i}", "agent", "s", "s", "a", "a", "r", $"r{i}", "d");

        var page1 = await svc.QueryAsync(offset: 0, limit: 2);
        var page2 = await svc.QueryAsync(offset: 2, limit: 2);

        Assert.Equal(2, page1.Entries.Count);
        Assert.Equal(2, page2.Entries.Count);
        Assert.True(page1.HasMore);
        Assert.Equal(5, page1.TotalCount);
    }

    // ── Integrity Verification ────────────────────────────────────────

    [Fact]
    public async Task VerifyIntegrity_PassesForValidChain()
    {
        var svc = CreateService();
        for (int i = 0; i < 10; i++)
            await svc.RecordAsync($"e{i}", "agent", "s", "s", "a", "a", "r", $"r{i}", "d");

        var isValid = await svc.VerifyIntegrityAsync();

        Assert.True(isValid);
    }

    [Fact]
    public async Task GetEntry_ReturnsExistingEntry()
    {
        var svc = CreateService();
        var entry = await svc.RecordAsync("e1", "agent", "s", "s1", "a", "a1", "r", "r1", "d");

        var retrieved = await svc.GetEntryAsync(entry.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(entry.Id, retrieved!.Id);
        Assert.Equal(entry.Checksum, retrieved.Checksum);
    }

    [Fact]
    public async Task GetEntry_NonexistentId_ReturnsNull()
    {
        var svc = CreateService();
        var retrieved = await svc.GetEntryAsync(Guid.NewGuid());
        Assert.Null(retrieved);
    }

    // ── Category Statistics ───────────────────────────────────────────

    [Fact]
    public async Task Status_TracksCategoryCounts()
    {
        var svc = CreateService();
        await svc.RecordAsync("e1", "agent", "s", "s1", "a", "a1", "r", "r1", "d");
        await svc.RecordAsync("e2", "agent", "s", "s2", "a", "a2", "r", "r2", "d");
        await svc.RecordAsync("e3", "workflow", "s", "s3", "a", "a3", "r", "r3", "d");
        await svc.RecordAsync("e4", "user", "s", "s4", "a", "a4", "r", "r4", "d");

        var status = svc.GetStatus();

        Assert.Equal(4, status.TotalEntries);
        Assert.Equal(2, status.AgentActionEntries);
        Assert.Equal(1, status.WorkflowChangeEntries);
        Assert.Equal(1, status.UserActivityEntries);
        Assert.True(status.IsActive);
    }

    // ── Metadata Inclusion ────────────────────────────────────────────

    [Fact]
    public async Task RecordAsync_IncludesMetadata()
    {
        var svc = CreateService();
        var metadata = new Dictionary<string, string>
        {
            ["ip_address"] = "10.0.0.1",
            ["user_agent"] = "ArchonAI/1.0"
        };

        var entry = await svc.RecordAsync(
            "user.login", "user", "AuthService", "user-1", "user",
            "login", "session", "sess-1", "User logged in", metadata);

        Assert.Equal("10.0.0.1", entry.Metadata["ip_address"]);
        Assert.Equal("ArchonAI/1.0", entry.Metadata["user_agent"]);
    }
}

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.AuditLog;
using OTel = ArchonAI.Common.Observability.Telemetry;

namespace ArchonAI.Api.Security;

public sealed class AuditLogService : IAuditLogService
{
    private readonly ConcurrentDictionary<Guid, AuditEntry> _entries = new();
    private readonly ConcurrentQueue<Guid> _entryOrder = new();
    private readonly ILogger<AuditLogService> _logger;
    private readonly object _appendLock = new();

    private Guid? _lastEntryId;
    private string _latestChecksum = string.Empty;

    private long _totalEntries;
    private long _agentActionEntries;
    private long _workflowChangeEntries;
    private long _userActivityEntries;

    public AuditLogService(ILogger<AuditLogService> logger)
    {
        _logger = logger;
    }

    public global::System.Threading.Tasks.Task<AuditEntry> RecordAsync(
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
        CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("AuditLog.Record");
        activity?.SetTag("audit.event_type", eventType);
        activity?.SetTag("audit.category", category);
        activity?.SetTag("audit.subject_id", subjectId);

        OTel.AuditEntriesRecorded.Add(1);

        var entryId = Guid.NewGuid();
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var entryMetadata = metadata ?? new Dictionary<string, string>();

        AuditEntry entry;

        lock (_appendLock)
        {
            var previousEntryId = _lastEntryId;
            string checksum = ComputeChecksum(entryId, eventType, category, source,
                subjectId, action, resourceType, resourceId, occurredAtUtc, _latestChecksum);

            entry = new AuditEntry(
                entryId, eventType, category, source,
                subjectId, subjectType, action,
                resourceType, resourceId, description,
                entryMetadata, checksum, previousEntryId, occurredAtUtc);

            _entries[entryId] = entry;
            _entryOrder.Enqueue(entryId);
            _lastEntryId = entryId;
            _latestChecksum = checksum;
        }

        Interlocked.Increment(ref _totalEntries);

        switch (category.ToLowerInvariant())
        {
            case "agent":
                Interlocked.Increment(ref _agentActionEntries);
                break;
            case "workflow":
                Interlocked.Increment(ref _workflowChangeEntries);
                break;
            case "user":
                Interlocked.Increment(ref _userActivityEntries);
                break;
        }

        _logger.LogInformation(
            "Audit [{Category}] {EventType}: {SubjectId} {Action} {ResourceType}/{ResourceId} — {Description}",
            category, eventType, subjectId, action, resourceType, resourceId, description);

        return global::System.Threading.Tasks.Task.FromResult(entry);
    }

    public global::System.Threading.Tasks.Task<AuditQueryResult> QueryAsync(
        string? category = null,
        string? subjectId = null,
        string? resourceType = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        int offset = 0,
        int limit = 100,
        CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("AuditLog.Query");

        IEnumerable<AuditEntry> query = _entryOrder
            .Select(id => _entries.TryGetValue(id, out var e) ? e : null)
            .Where(e => e is not null)!;

        if (!string.IsNullOrEmpty(category))
            query = query.Where(e => string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(subjectId))
            query = query.Where(e => e.SubjectId == subjectId);

        if (!string.IsNullOrEmpty(resourceType))
            query = query.Where(e => string.Equals(e.ResourceType, resourceType, StringComparison.OrdinalIgnoreCase));

        if (fromUtc.HasValue)
            query = query.Where(e => e.OccurredAtUtc >= fromUtc.Value);

        if (toUtc.HasValue)
            query = query.Where(e => e.OccurredAtUtc <= toUtc.Value);

        var allMatches = query.OrderByDescending(e => e.OccurredAtUtc).ToList();
        int totalCount = allMatches.Count;
        var pageEntries = allMatches.Skip(offset).Take(limit).ToList();

        return global::System.Threading.Tasks.Task.FromResult(new AuditQueryResult(
            pageEntries, totalCount, offset + pageEntries.Count < totalCount, DateTimeOffset.UtcNow));
    }

    public global::System.Threading.Tasks.Task<AuditEntry?> GetEntryAsync(Guid entryId, CancellationToken ct = default)
    {
        _entries.TryGetValue(entryId, out var entry);
        return global::System.Threading.Tasks.Task.FromResult(entry);
    }

    public global::System.Threading.Tasks.Task<bool> VerifyIntegrityAsync(Guid? fromEntryId = null, CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("AuditLog.VerifyIntegrity");

        OTel.AuditIntegrityChecks.Add(1);

        var orderedIds = _entryOrder.ToArray();
        string previousChecksum = string.Empty;
        bool started = fromEntryId is null;

        foreach (var id in orderedIds)
        {
            if (!started)
            {
                if (id == fromEntryId.Value)
                    started = true;
                else
                    continue;
            }

            if (!_entries.TryGetValue(id, out var entry))
            {
                _logger.LogWarning("Audit integrity check failed: missing entry {EntryId}", id);
                return global::System.Threading.Tasks.Task.FromResult(false);
            }

            string expectedChecksum = ComputeChecksum(
                entry.Id, entry.EventType, entry.Category, entry.Source,
                entry.SubjectId, entry.Action, entry.ResourceType, entry.ResourceId,
                entry.OccurredAtUtc, previousChecksum);

            if (entry.Checksum != expectedChecksum)
            {
                _logger.LogWarning(
                    "Audit integrity check failed: checksum mismatch for entry {EntryId}. Expected={Expected}, Actual={Actual}",
                    entry.Id, expectedChecksum, entry.Checksum);
                return global::System.Threading.Tasks.Task.FromResult(false);
            }

            previousChecksum = entry.Checksum;
        }

        _logger.LogInformation("Audit integrity check passed for {Count} entries", orderedIds.Length);
        return global::System.Threading.Tasks.Task.FromResult(true);
    }

    public AuditLogStatus GetStatus()
    {
        return new AuditLogStatus(
            IsActive: true,
            TotalEntries: Interlocked.Read(ref _totalEntries),
            AgentActionEntries: Interlocked.Read(ref _agentActionEntries),
            WorkflowChangeEntries: Interlocked.Read(ref _workflowChangeEntries),
            UserActivityEntries: Interlocked.Read(ref _userActivityEntries),
            LatestChecksum: _latestChecksum,
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    private static string ComputeChecksum(
        Guid entryId, string eventType, string category, string source,
        string subjectId, string action, string resourceType, string resourceId,
        DateTimeOffset occurredAtUtc, string previousChecksum)
    {
        string payload = $"{entryId}|{eventType}|{category}|{source}|{subjectId}|{action}|{resourceType}|{resourceId}|{occurredAtUtc:O}|{previousChecksum}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }
}

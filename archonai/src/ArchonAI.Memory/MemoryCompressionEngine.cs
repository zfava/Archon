using System.Collections.Concurrent;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Memory;

public sealed class MemoryCompressionEngine : IMemoryCompressionEngine
{
    private readonly IMemoryStore _memoryStore;
    private readonly MemoryCompressionOptions _options;
    private readonly ILogger<MemoryCompressionEngine> _logger;

    private long _compressionRuns;
    private long _totalDuplicatesRemoved;
    private long _totalClustersFormed;
    private long _totalSummariesCreated;

    public MemoryCompressionEngine(
        IMemoryStore memoryStore,
        IOptions<MemoryCompressionOptions> options,
        ILogger<MemoryCompressionEngine> logger)
    {
        _memoryStore = memoryStore;
        _options = options.Value;
        _logger = logger;
    }

    public long CompressionRuns => Interlocked.Read(ref _compressionRuns);
    public long TotalDuplicatesRemoved => Interlocked.Read(ref _totalDuplicatesRemoved);
    public long TotalClustersFormed => Interlocked.Read(ref _totalClustersFormed);
    public long TotalSummariesCreated => Interlocked.Read(ref _totalSummariesCreated);

    public async global::System.Threading.Tasks.Task<CompressionResult> CompressAsync(
        string scope, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("memory.compression");
        activity?.SetTag("scope", scope);

        Interlocked.Increment(ref _compressionRuns);
        Telemetry.MemoryCompressionRuns.Add(1);

        var records = await _memoryStore.QueryByScopeAsync(scope, cancellationToken);
        int originalCount = records.Count;

        if (originalCount == 0)
        {
            return new CompressionResult(0, 0, 0, 0, 0, DateTimeOffset.UtcNow);
        }

        // Step 1: Remove duplicates
        int duplicatesRemoved = await DeduplicateInternalAsync(scope, records, cancellationToken);
        Interlocked.Add(ref _totalDuplicatesRemoved, duplicatesRemoved);

        // Step 2: Re-query after dedup and cluster related records
        var deduplicatedRecords = await _memoryStore.QueryByScopeAsync(scope, cancellationToken);
        var clusters = ClusterRecords(deduplicatedRecords);
        Interlocked.Add(ref _totalClustersFormed, clusters.Count);

        // Step 3: Summarize large clusters
        int summariesCreated = 0;
        foreach (var cluster in clusters)
        {
            if (cluster.MemberRecordIds.Count >= _options.MinClusterSize)
            {
                await SummarizeInternalAsync(scope, cluster, deduplicatedRecords, cancellationToken);
                summariesCreated++;
            }
        }
        Interlocked.Add(ref _totalSummariesCreated, summariesCreated);

        Telemetry.MemoryDuplicatesRemoved.Add(duplicatesRemoved);
        Telemetry.MemoryClustersFormed.Add(clusters.Count);
        Telemetry.MemorySummariesCreated.Add(summariesCreated);

        var compressedRecords = await _memoryStore.QueryByScopeAsync(scope, cancellationToken);

        _logger.LogInformation(
            "Memory compression for scope '{Scope}': {Original} -> {Compressed} records, " +
            "{Duplicates} duplicates removed, {Clusters} clusters, {Summaries} summaries",
            scope, originalCount, compressedRecords.Count, duplicatesRemoved, clusters.Count, summariesCreated);

        return new CompressionResult(
            OriginalCount: originalCount,
            CompressedCount: compressedRecords.Count,
            DuplicatesRemoved: duplicatesRemoved,
            ClustersFormed: clusters.Count,
            SummariesCreated: summariesCreated,
            CompressedAtUtc: DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task<int> DeduplicateAsync(
        string scope, CancellationToken cancellationToken = default)
    {
        var records = await _memoryStore.QueryByScopeAsync(scope, cancellationToken);
        int removed = await DeduplicateInternalAsync(scope, records, cancellationToken);
        Interlocked.Add(ref _totalDuplicatesRemoved, removed);
        Telemetry.MemoryDuplicatesRemoved.Add(removed);
        return removed;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<MemoryCluster>> ClusterAsync(
        string scope, CancellationToken cancellationToken = default)
    {
        var records = await _memoryStore.QueryByScopeAsync(scope, cancellationToken);
        var clusters = ClusterRecords(records);
        Interlocked.Add(ref _totalClustersFormed, clusters.Count);
        Telemetry.MemoryClustersFormed.Add(clusters.Count);
        return clusters;
    }

    public async global::System.Threading.Tasks.Task<MemorySummary> SummarizeAsync(
        string scope, IReadOnlyList<Guid> sourceRecordIds, CancellationToken cancellationToken = default)
    {
        var records = await _memoryStore.QueryByScopeAsync(scope, cancellationToken);
        var sourceRecords = records.Where(r => sourceRecordIds.Contains(r.Id)).ToList();

        var summary = CreateSummary(scope, sourceRecords);

        // Persist the summary as a new memory record
        await _memoryStore.SaveAsync(new MemoryRecord(
            Id: summary.Id,
            MemoryType: "summary",
            Scope: scope,
            Content: summary.Content,
            Metadata: summary.Metadata,
            CreatedAtUtc: summary.CreatedAtUtc,
            ExpiresAtUtc: null), cancellationToken);

        Interlocked.Increment(ref _totalSummariesCreated);
        Telemetry.MemorySummariesCreated.Add(1);
        return summary;
    }

    private async global::System.Threading.Tasks.Task<int> DeduplicateInternalAsync(
        string scope, IReadOnlyList<MemoryRecord> records, CancellationToken cancellationToken)
    {
        // Identify duplicates using content fingerprinting (simhash-like approach)
        var seen = new Dictionary<string, MemoryRecord>();
        var duplicateIds = new List<Guid>();

        foreach (var record in records.OrderBy(r => r.CreatedAtUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string fingerprint = ComputeFingerprint(record.Content);

            if (seen.TryGetValue(fingerprint, out var existing))
            {
                // Keep the older record, mark the newer as duplicate
                duplicateIds.Add(record.Id);
            }
            else
            {
                // Check fuzzy matches against all seen records
                bool isDuplicate = false;
                foreach (var (_, seenRecord) in seen)
                {
                    if (ComputeContentSimilarity(record.Content, seenRecord.Content) >= _options.DuplicateSimilarityThreshold)
                    {
                        duplicateIds.Add(record.Id);
                        isDuplicate = true;
                        break;
                    }
                }

                if (!isDuplicate)
                {
                    seen[fingerprint] = record;
                }
            }
        }

        // Mark duplicates as expired (soft delete via expiration)
        foreach (var dupId in duplicateIds)
        {
            var original = records.First(r => r.Id == dupId);
            var expired = new MemoryRecord(
                Id: original.Id,
                MemoryType: original.MemoryType,
                Scope: original.Scope,
                Content: original.Content,
                Metadata: original.Metadata,
                CreatedAtUtc: original.CreatedAtUtc,
                ExpiresAtUtc: DateTimeOffset.UtcNow);
            // Re-save with expiration to soft-delete
            await _memoryStore.SaveAsync(expired, cancellationToken);
        }

        _logger.LogDebug("Deduplicated scope '{Scope}': removed {Count} duplicates", scope, duplicateIds.Count);
        return duplicateIds.Count;
    }

    private IReadOnlyList<MemoryCluster> ClusterRecords(IReadOnlyList<MemoryRecord> records)
    {
        // Simple single-pass clustering by memory type + content similarity
        var clusters = new List<MemoryCluster>();
        var assigned = new HashSet<Guid>();

        var eligible = records
            .Where(r => !r.ExpiresAtUtc.HasValue || r.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .OrderBy(r => r.MemoryType)
            .ThenBy(r => r.CreatedAtUtc)
            .ToList();

        for (int i = 0; i < eligible.Count; i++)
        {
            if (assigned.Contains(eligible[i].Id)) continue;

            var clusterMembers = new List<MemoryRecord> { eligible[i] };
            assigned.Add(eligible[i].Id);

            for (int j = i + 1; j < eligible.Count && clusterMembers.Count < _options.MaxClusterSize; j++)
            {
                if (assigned.Contains(eligible[j].Id)) continue;

                // Cluster by same memory type and content similarity
                if (eligible[i].MemoryType == eligible[j].MemoryType &&
                    ComputeContentSimilarity(eligible[i].Content, eligible[j].Content) >= _options.ClusterSimilarityThreshold)
                {
                    clusterMembers.Add(eligible[j]);
                    assigned.Add(eligible[j].Id);
                }
            }

            if (clusterMembers.Count >= _options.MinClusterSize)
            {
                var topic = ExtractTopic(clusterMembers);
                clusters.Add(new MemoryCluster(
                    Id: Guid.NewGuid(),
                    Scope: eligible[i].Scope,
                    Topic: topic,
                    MemberRecordIds: clusterMembers.Select(m => m.Id).ToList(),
                    Summary: $"Cluster of {clusterMembers.Count} {eligible[i].MemoryType} records about: {topic}",
                    Metadata: new Dictionary<string, string>
                    {
                        ["memoryType"] = eligible[i].MemoryType,
                        ["memberCount"] = clusterMembers.Count.ToString()
                    },
                    CreatedAtUtc: DateTimeOffset.UtcNow));
            }
        }

        return clusters;
    }

    private async global::System.Threading.Tasks.Task SummarizeInternalAsync(
        string scope, MemoryCluster cluster, IReadOnlyList<MemoryRecord> allRecords,
        CancellationToken cancellationToken)
    {
        var sourceRecords = allRecords
            .Where(r => cluster.MemberRecordIds.Contains(r.Id))
            .Take(_options.MaxSummarySourceRecords)
            .ToList();

        var summary = CreateSummary(scope, sourceRecords);

        await _memoryStore.SaveAsync(new MemoryRecord(
            Id: summary.Id,
            MemoryType: "summary",
            Scope: scope,
            Content: summary.Content,
            Metadata: summary.Metadata,
            CreatedAtUtc: summary.CreatedAtUtc,
            ExpiresAtUtc: null), cancellationToken);
    }

    private static MemorySummary CreateSummary(string scope, IReadOnlyList<MemoryRecord> sourceRecords)
    {
        // Extract key phrases and combine into a condensed summary
        var contentParts = sourceRecords
            .Select(r => r.Content.Length > 200 ? r.Content[..200] : r.Content)
            .ToList();

        var combinedContent = string.Join(" | ", contentParts);
        if (combinedContent.Length > 2000)
            combinedContent = combinedContent[..2000];

        var metadata = new Dictionary<string, string>
        {
            ["sourceCount"] = sourceRecords.Count.ToString(),
            ["sourceIds"] = string.Join(",", sourceRecords.Select(r => r.Id)),
            ["oldestRecord"] = sourceRecords.Min(r => r.CreatedAtUtc).ToString("O"),
            ["newestRecord"] = sourceRecords.Max(r => r.CreatedAtUtc).ToString("O")
        };

        string memoryType = sourceRecords.FirstOrDefault()?.MemoryType ?? "unknown";

        return new MemorySummary(
            Id: Guid.NewGuid(),
            Scope: scope,
            OriginalMemoryType: memoryType,
            SourceRecordIds: sourceRecords.Select(r => r.Id).ToList(),
            Content: combinedContent,
            Metadata: metadata,
            CreatedAtUtc: DateTimeOffset.UtcNow);
    }

    private static string ComputeFingerprint(string content)
    {
        // Normalize and hash content for exact-match deduplication
        var normalized = content.Trim().ToLowerInvariant();
        int hash = normalized.GetHashCode(StringComparison.Ordinal);
        return hash.ToString("X8");
    }

    private static double ComputeContentSimilarity(string a, string b)
    {
        // Jaccard similarity on word trigrams for fuzzy matching
        var trigramsA = GetWordTrigrams(a);
        var trigramsB = GetWordTrigrams(b);

        if (trigramsA.Count == 0 && trigramsB.Count == 0) return 1.0;
        if (trigramsA.Count == 0 || trigramsB.Count == 0) return 0.0;

        int intersection = trigramsA.Intersect(trigramsB).Count();
        int union = trigramsA.Union(trigramsB).Count();

        return union > 0 ? (double)intersection / union : 0.0;
    }

    private static HashSet<string> GetWordTrigrams(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var trigrams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i <= words.Length - 3; i++)
        {
            trigrams.Add($"{words[i]} {words[i + 1]} {words[i + 2]}");
        }

        // Also add individual words for short texts
        if (words.Length < 6)
        {
            foreach (var word in words)
                trigrams.Add(word);
        }

        return trigrams;
    }

    private static string ExtractTopic(IReadOnlyList<MemoryRecord> records)
    {
        // Extract the most common metadata values as topic
        var topics = records
            .SelectMany(r => r.Metadata.Values)
            .Where(v => !string.IsNullOrWhiteSpace(v) && v.Length < 100)
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => g.Key);

        var topic = string.Join(", ", topics);
        return string.IsNullOrWhiteSpace(topic) ? records.First().MemoryType : topic;
    }
}

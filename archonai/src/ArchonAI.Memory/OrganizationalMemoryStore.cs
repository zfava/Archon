using System.Collections.Concurrent;
using System.Diagnostics;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Knowledge;
using ArchonAI.Core.Models.Memory;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Memory;

/// <summary>
/// Long-term organizational memory that stores strategies, outcomes, and patterns.
/// Each entry is persisted in both the MemoryStore (for embedding-based semantic search)
/// and the KnowledgeGraph (for relationship traversal). Semantic search combines relevance,
/// recency, importance, and graph context into a single ranking score.
/// </summary>
public sealed class OrganizationalMemoryStore : IOrganizationalMemoryStore
{
    private readonly IMemoryStore _memoryStore;
    private readonly IKnowledgeGraphStore _knowledgeStore;
    private readonly IMemoryRetrievalOptimizer _retriever;
    private readonly IEventBus _eventBus;
    private readonly ILogger<OrganizationalMemoryStore> _logger;

    private const string MemoryScope = "org-memory";
    private const string GraphNodePrefix = "org_memory:";

    // Local index for queries that don't need embeddings
    private readonly ConcurrentDictionary<Guid, OrganizationalMemoryEntry> _entries = new();

    public OrganizationalMemoryStore(
        IMemoryStore memoryStore,
        IKnowledgeGraphStore knowledgeStore,
        IMemoryRetrievalOptimizer retriever,
        IEventBus eventBus,
        ILogger<OrganizationalMemoryStore> logger)
    {
        _memoryStore = memoryStore;
        _knowledgeStore = knowledgeStore;
        _retriever = retriever;
        _eventBus = eventBus;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  Store
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task StoreAsync(
        OrganizationalMemoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _entries[entry.EntryId] = entry;

        // 1. Persist as MemoryRecord for semantic search
        var metadata = new Dictionary<string, string>(entry.Properties, StringComparer.OrdinalIgnoreCase)
        {
            ["entryType"] = entry.EntryType.ToString(),
            ["category"] = entry.Category,
            ["importance"] = entry.Importance.ToString("F3"),
            ["tags"] = string.Join(",", entry.Tags),
            ["occurredAtUtc"] = entry.OccurredAtUtc.ToString("O")
        };

        await _memoryStore.SaveAsync(new MemoryRecord(
            Id: entry.EntryId,
            MemoryType: $"org-{entry.EntryType.ToString().ToLowerInvariant()}",
            Scope: MemoryScope,
            Content: entry.Summary,
            Metadata: metadata,
            CreatedAtUtc: entry.StoredAtUtc,
            ExpiresAtUtc: null), cancellationToken);

        // 2. Persist as KnowledgeGraph node with relationships
        string nodeId = $"{GraphNodePrefix}{entry.EntryId}";
        await _knowledgeStore.UpsertNodeAsync(new KnowledgeNode(
            NodeId: nodeId,
            NodeType: $"org_memory_{entry.EntryType.ToString().ToLowerInvariant()}",
            DisplayName: TruncateForDisplay(entry.Summary),
            Properties: metadata,
            UpdatedAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        // 3. Create category relationship
        string categoryNodeId = $"org_category:{entry.Category}";
        await _knowledgeStore.UpsertNodeAsync(new KnowledgeNode(
            NodeId: categoryNodeId,
            NodeType: "org_memory_category",
            DisplayName: entry.Category,
            Properties: new Dictionary<string, string>
            {
                ["category"] = entry.Category
            },
            UpdatedAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        await _knowledgeStore.UpsertRelationshipAsync(new KnowledgeRelationship(
            RelationshipId: $"{nodeId}->category:{entry.Category}",
            FromNodeId: nodeId,
            RelationshipType: "belongs_to_category",
            ToNodeId: categoryNodeId,
            Properties: new Dictionary<string, string>
            {
                ["entryType"] = entry.EntryType.ToString()
            },
            UpdatedAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        // 4. Create tag relationships
        foreach (var tag in entry.Tags)
        {
            string tagNodeId = $"org_tag:{tag}";
            await _knowledgeStore.UpsertNodeAsync(new KnowledgeNode(
                NodeId: tagNodeId,
                NodeType: "org_memory_tag",
                DisplayName: tag,
                Properties: new Dictionary<string, string> { ["tag"] = tag },
                UpdatedAtUtc: DateTimeOffset.UtcNow), cancellationToken);

            await _knowledgeStore.UpsertRelationshipAsync(new KnowledgeRelationship(
                RelationshipId: $"{nodeId}->tag:{tag}",
                FromNodeId: nodeId,
                RelationshipType: "tagged_with",
                ToNodeId: tagNodeId,
                Properties: new Dictionary<string, string>(),
                UpdatedAtUtc: DateTimeOffset.UtcNow), cancellationToken);
        }

        // 5. Link outcomes to their strategies
        if (entry.EntryType == OrganizationalMemoryType.Outcome
            && entry.Properties.TryGetValue("strategyEntryId", out var strategyId))
        {
            string strategyNodeId = $"{GraphNodePrefix}{strategyId}";
            await _knowledgeStore.UpsertRelationshipAsync(new KnowledgeRelationship(
                RelationshipId: $"{nodeId}->strategy:{strategyId}",
                FromNodeId: nodeId,
                RelationshipType: "outcome_of_strategy",
                ToNodeId: strategyNodeId,
                Properties: new Dictionary<string, string>
                {
                    ["success"] = entry.Properties.GetValueOrDefault("success", "unknown")
                },
                UpdatedAtUtc: DateTimeOffset.UtcNow), cancellationToken);
        }

        // 6. Link patterns to their source entries
        if (entry.EntryType == OrganizationalMemoryType.Pattern
            && entry.Properties.TryGetValue("sourceEntryIds", out var sourceIds))
        {
            foreach (var sourceId in sourceIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string sourceNodeId = $"{GraphNodePrefix}{sourceId}";
                await _knowledgeStore.UpsertRelationshipAsync(new KnowledgeRelationship(
                    RelationshipId: $"{nodeId}->source:{sourceId}",
                    FromNodeId: nodeId,
                    RelationshipType: "derived_from",
                    ToNodeId: sourceNodeId,
                    Properties: new Dictionary<string, string>(),
                    UpdatedAtUtc: DateTimeOffset.UtcNow), cancellationToken);
            }
        }

        _logger.LogInformation(
            "Stored organizational memory: type={Type}, category={Category}, id={Id}",
            entry.EntryType, entry.Category, entry.EntryId);
    }

    // ══════════════════════════════════════════════════════════════
    //  Semantic search
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<OrganizationalMemorySearchResult> SearchAsync(
        string queryText,
        OrganizationalMemoryType? filterType = null,
        string? filterCategory = null,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();

        // Generate a simple embedding from query text (bag-of-words tf vector)
        var queryEmbedding = GenerateSimpleEmbedding(queryText);

        // Use the retrieval optimizer for semantic + recency + graph search
        var retrievalResult = await _retriever.SearchAsync(
            MemoryScope, queryEmbedding, topK * 3, cancellationToken);

        // Map MemoryStore results back to organizational entries and apply filters
        var now = DateTimeOffset.UtcNow;
        var queryTerms = ExtractTerms(queryText);

        var scored = new List<ScoredOrganizationalMemory>();
        foreach (var scoredRecord in retrievalResult.Records)
        {
            var record = scoredRecord.Record;

            // Try to find the full entry in local index
            if (!_entries.TryGetValue(record.Id, out var orgEntry))
            {
                // Reconstruct from memory record metadata
                orgEntry = ReconstructEntry(record);
                if (orgEntry is not null)
                    _entries.TryAdd(record.Id, orgEntry);
            }

            if (orgEntry is null) continue;

            // Apply filters
            if (filterType.HasValue && orgEntry.EntryType != filterType.Value) continue;
            if (!string.IsNullOrWhiteSpace(filterCategory)
                && !orgEntry.Category.Equals(filterCategory, StringComparison.OrdinalIgnoreCase)) continue;

            // Compute composite score
            double semanticScore = scoredRecord.RelevanceScore;

            // Term-match boost for entries that contain query keywords
            double termMatchBoost = ComputeTermMatchScore(orgEntry.Summary, orgEntry.Tags, queryTerms);
            semanticScore = Math.Min(1.0, semanticScore + (termMatchBoost * 0.3));

            double ageHours = Math.Max(0, (now - orgEntry.OccurredAtUtc).TotalHours);
            double recencyScore = Math.Exp(-0.005 * ageHours); // Slow decay for long-term memory

            double importanceScore = orgEntry.Importance;

            double graphScore = scoredRecord.GraphBoost ?? 0.0;

            double finalScore =
                (semanticScore * 0.40) +
                (recencyScore * 0.15) +
                (importanceScore * 0.25) +
                (graphScore * 0.20);

            scored.Add(new ScoredOrganizationalMemory(
                Entry: orgEntry,
                SemanticScore: Math.Round(semanticScore, 4),
                RecencyScore: Math.Round(recencyScore, 4),
                ImportanceScore: Math.Round(importanceScore, 4),
                GraphScore: Math.Round(graphScore, 4),
                FinalScore: Math.Round(finalScore, 4)));
        }

        // Also search local entries with text matching for entries not yet in the embedding index
        foreach (var entry in _entries.Values)
        {
            if (scored.Any(s => s.Entry.EntryId == entry.EntryId)) continue;
            if (filterType.HasValue && entry.EntryType != filterType.Value) continue;
            if (!string.IsNullOrWhiteSpace(filterCategory)
                && !entry.Category.Equals(filterCategory, StringComparison.OrdinalIgnoreCase)) continue;

            double termScore = ComputeTermMatchScore(entry.Summary, entry.Tags, queryTerms);
            if (termScore < 0.1) continue;

            double ageHours = Math.Max(0, (now - entry.OccurredAtUtc).TotalHours);
            double recencyScore = Math.Exp(-0.005 * ageHours);

            double finalScore =
                (termScore * 0.40) +
                (recencyScore * 0.15) +
                (entry.Importance * 0.25);

            scored.Add(new ScoredOrganizationalMemory(
                Entry: entry,
                SemanticScore: Math.Round(termScore, 4),
                RecencyScore: Math.Round(recencyScore, 4),
                ImportanceScore: Math.Round(entry.Importance, 4),
                GraphScore: 0,
                FinalScore: Math.Round(finalScore, 4)));
        }

        var topResults = scored
            .OrderByDescending(s => s.FinalScore)
            .Take(topK)
            .ToList();

        sw.Stop();

        // Publish inspection events for each retrieved memory so the inspection system
        // traces which organizational memories were surfaced during retrieval.
        foreach (var match in topResults)
        {
            _ = _eventBus.PublishAsync(new SystemEvent(
                Guid.NewGuid(),
                "inspection.memory-reference-recorded",
                "OrganizationalMemoryStore",
                match.Entry.EntryId,
                new Dictionary<string, string>
                {
                    ["subjectType"] = "org-memory-search",
                    ["subjectId"] = queryText,
                    ["memoryRecordId"] = match.Entry.EntryId.ToString(),
                    ["scope"] = MemoryScope,
                    ["category"] = match.Entry.Category,
                    ["summary"] = match.Entry.Summary.Length <= 120
                        ? match.Entry.Summary : match.Entry.Summary[..117] + "...",
                    ["relevanceScore"] = match.FinalScore.ToString("F4"),
                }.AsReadOnly(),
                DateTimeOffset.UtcNow), cancellationToken).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.LogWarning(t.Exception,
                        "Failed to publish inspection.memory-reference-recorded for entry {EntryId}",
                        match.Entry.EntryId);
            }, global::System.Threading.Tasks.TaskScheduler.Default);
        }

        return new OrganizationalMemorySearchResult(
            Matches: topResults,
            TotalCandidates: scored.Count,
            SearchDurationMs: Math.Round(sw.Elapsed.TotalMilliseconds, 2),
            SearchStrategy: "semantic+term+recency+importance+graph");
    }

    // ══════════════════════════════════════════════════════════════
    //  Timeline queries
    // ══════════════════════════════════════════════════════════════

    public global::System.Threading.Tasks.Task<OrganizationalTimeline> GetTimelineAsync(
        OrganizationalMemoryType entryType,
        string? category = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filtered = _entries.Values
            .Where(e => e.EntryType == entryType)
            .Where(e => string.IsNullOrWhiteSpace(category)
                || e.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.OccurredAtUtc)
            .ToList();

        var limited = filtered.Take(limit).ToList();

        return global::System.Threading.Tasks.Task.FromResult(new OrganizationalTimeline(
            Category: category ?? "all",
            Entries: limited,
            TotalCount: filtered.Count,
            EarliestEntry: filtered.Count > 0 ? filtered[^1].OccurredAtUtc : null,
            LatestEntry: filtered.Count > 0 ? filtered[0].OccurredAtUtc : null));
    }

    // ══════════════════════════════════════════════════════════════
    //  Analysis — extract insights from memory
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<OrganizationalMemoryReport> AnalyzeAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var allEntries = _entries.Values.ToList();
        var strategies = allEntries.Where(e => e.EntryType == OrganizationalMemoryType.Strategy).ToList();
        var outcomes = allEntries.Where(e => e.EntryType == OrganizationalMemoryType.Outcome).ToList();
        var patterns = allEntries.Where(e => e.EntryType == OrganizationalMemoryType.Pattern).ToList();
        var lessons = allEntries.Where(e => e.EntryType == OrganizationalMemoryType.Lesson).ToList();

        var insights = new List<OrganizationalInsight>();
        int knowledgeNodesCreated = 0;

        // Insight 1: Strategy effectiveness — find strategies with linked outcomes
        var strategyOutcomes = outcomes
            .Where(o => o.Properties.ContainsKey("strategyEntryId"))
            .GroupBy(o => o.Properties["strategyEntryId"])
            .ToList();

        foreach (var group in strategyOutcomes)
        {
            var outcomeList = group.ToList();
            int successes = outcomeList.Count(o =>
                o.Properties.TryGetValue("success", out var s) && s.Equals("true", StringComparison.OrdinalIgnoreCase));
            double successRate = outcomeList.Count > 0 ? successes / (double)outcomeList.Count : 0;

            if (outcomeList.Count >= 3)
            {
                string strategyName = strategies
                    .FirstOrDefault(s => s.EntryId.ToString() == group.Key)?.Category ?? group.Key;

                insights.Add(new OrganizationalInsight(
                    InsightType: successRate >= 0.8 ? "effective_strategy" : "ineffective_strategy",
                    Category: strategyName,
                    Summary: successRate >= 0.8
                        ? $"Strategy '{strategyName}' is effective with {successRate:P0} success rate across {outcomeList.Count} outcomes"
                        : $"Strategy '{strategyName}' is underperforming with {successRate:P0} success rate across {outcomeList.Count} outcomes",
                    Confidence: Math.Min(1.0, outcomeList.Count / 10.0),
                    SupportingEntries: outcomeList.Count,
                    Evidence: new Dictionary<string, string>
                    {
                        ["successRate"] = successRate.ToString("F4"),
                        ["totalOutcomes"] = outcomeList.Count.ToString(),
                        ["successes"] = successes.ToString()
                    },
                    GeneratedAtUtc: DateTimeOffset.UtcNow));
            }
        }

        // Insight 2: Recurring patterns — find patterns with high frequency
        var patternsByCategory = patterns
            .GroupBy(p => p.Category, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 2)
            .ToList();

        foreach (var group in patternsByCategory)
        {
            double avgImportance = group.Average(p => p.Importance);
            insights.Add(new OrganizationalInsight(
                InsightType: "recurring_pattern",
                Category: group.Key,
                Summary: $"Pattern '{group.Key}' recurs across {group.Count()} entries with average importance {avgImportance:F2}",
                Confidence: Math.Min(1.0, group.Count() / 5.0),
                SupportingEntries: group.Count(),
                Evidence: new Dictionary<string, string>
                {
                    ["occurrences"] = group.Count().ToString(),
                    ["avgImportance"] = avgImportance.ToString("F4"),
                    ["latestOccurrence"] = group.Max(p => p.OccurredAtUtc).ToString("O")
                },
                GeneratedAtUtc: DateTimeOffset.UtcNow));
        }

        // Insight 3: Lessons by category frequency
        var lessonsByCategory = lessons
            .GroupBy(l => l.Category, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 2)
            .ToList();

        foreach (var group in lessonsByCategory)
        {
            insights.Add(new OrganizationalInsight(
                InsightType: "lesson_cluster",
                Category: group.Key,
                Summary: $"{group.Count()} lessons recorded for '{group.Key}' — indicates area needing attention",
                Confidence: Math.Min(1.0, group.Count() / 5.0),
                SupportingEntries: group.Count(),
                Evidence: new Dictionary<string, string>
                {
                    ["lessonCount"] = group.Count().ToString()
                },
                GeneratedAtUtc: DateTimeOffset.UtcNow));
        }

        // Insight 4: Category with declining outcomes
        var recentOutcomes = outcomes
            .Where(o => o.OccurredAtUtc >= DateTimeOffset.UtcNow.AddDays(-7))
            .ToList();
        var olderOutcomes = outcomes
            .Where(o => o.OccurredAtUtc < DateTimeOffset.UtcNow.AddDays(-7)
                && o.OccurredAtUtc >= DateTimeOffset.UtcNow.AddDays(-30))
            .ToList();

        if (recentOutcomes.Count >= 3 && olderOutcomes.Count >= 3)
        {
            double recentSuccessRate = recentOutcomes.Count(o =>
                o.Properties.TryGetValue("success", out var s) && s.Equals("true", StringComparison.OrdinalIgnoreCase))
                / (double)recentOutcomes.Count;
            double olderSuccessRate = olderOutcomes.Count(o =>
                o.Properties.TryGetValue("success", out var s) && s.Equals("true", StringComparison.OrdinalIgnoreCase))
                / (double)olderOutcomes.Count;

            if (recentSuccessRate < olderSuccessRate - 0.15)
            {
                insights.Add(new OrganizationalInsight(
                    InsightType: "declining_performance",
                    Category: "overall",
                    Summary: $"Recent outcome success rate ({recentSuccessRate:P0}) has declined compared to prior period ({olderSuccessRate:P0})",
                    Confidence: 0.7,
                    SupportingEntries: recentOutcomes.Count + olderOutcomes.Count,
                    Evidence: new Dictionary<string, string>
                    {
                        ["recentSuccessRate"] = recentSuccessRate.ToString("F4"),
                        ["olderSuccessRate"] = olderSuccessRate.ToString("F4"),
                        ["recentCount"] = recentOutcomes.Count.ToString(),
                        ["olderCount"] = olderOutcomes.Count.ToString()
                    },
                    GeneratedAtUtc: DateTimeOffset.UtcNow));
            }
        }

        // Persist insights as knowledge graph nodes
        foreach (var insight in insights)
        {
            string insightNodeId = $"org_insight:{insight.InsightType}:{insight.Category}";
            await _knowledgeStore.UpsertNodeAsync(new KnowledgeNode(
                NodeId: insightNodeId,
                NodeType: "org_memory_insight",
                DisplayName: TruncateForDisplay(insight.Summary),
                Properties: new Dictionary<string, string>(insight.Evidence)
                {
                    ["insightType"] = insight.InsightType,
                    ["category"] = insight.Category,
                    ["confidence"] = insight.Confidence.ToString("F4"),
                    ["supportingEntries"] = insight.SupportingEntries.ToString()
                },
                UpdatedAtUtc: DateTimeOffset.UtcNow), cancellationToken);
            knowledgeNodesCreated++;

            // Link insight to its category
            string categoryNodeId = $"org_category:{insight.Category}";
            await _knowledgeStore.UpsertRelationshipAsync(new KnowledgeRelationship(
                RelationshipId: $"{insightNodeId}->category:{insight.Category}",
                FromNodeId: insightNodeId,
                RelationshipType: "insight_for_category",
                ToNodeId: categoryNodeId,
                Properties: new Dictionary<string, string>
                {
                    ["confidence"] = insight.Confidence.ToString("F4")
                },
                UpdatedAtUtc: DateTimeOffset.UtcNow), cancellationToken);
        }

        _logger.LogInformation(
            "Organizational memory analysis: {Total} entries, {Insights} insights, {Nodes} knowledge nodes",
            allEntries.Count, insights.Count, knowledgeNodesCreated);

        return new OrganizationalMemoryReport(
            ReportId: Guid.NewGuid(),
            TotalEntries: allEntries.Count,
            StrategyEntries: strategies.Count,
            OutcomeEntries: outcomes.Count,
            PatternEntries: patterns.Count,
            LessonEntries: lessons.Count,
            KnowledgeNodesCreated: knowledgeNodesCreated,
            Insights: insights,
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    // ══════════════════════════════════════════════════════════════
    //  Graph relationship queries
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<IReadOnlyList<OrganizationalMemoryEntry>> GetRelatedEntriesAsync(
        string knowledgeNodeId,
        CancellationToken cancellationToken = default)
    {
        var relatedNodes = await _knowledgeStore.QueryRelatedNodesAsync(
            knowledgeNodeId, relationshipType: null, cancellationToken);

        var results = new List<OrganizationalMemoryEntry>();
        foreach (var node in relatedNodes)
        {
            if (!node.NodeId.StartsWith(GraphNodePrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string idPart = node.NodeId[GraphNodePrefix.Length..];
            if (Guid.TryParse(idPart, out var entryId) && _entries.TryGetValue(entryId, out var entry))
            {
                results.Add(entry);
            }
        }

        return results.OrderByDescending(e => e.OccurredAtUtc).ToList();
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private static OrganizationalMemoryEntry? ReconstructEntry(MemoryRecord record)
    {
        if (!record.Metadata.TryGetValue("entryType", out var typeStr)
            || !Enum.TryParse<OrganizationalMemoryType>(typeStr, ignoreCase: true, out var entryType))
        {
            return null;
        }

        string category = record.Metadata.GetValueOrDefault("category") ?? "unknown";
        double importance = double.TryParse(
            record.Metadata.GetValueOrDefault("importance"), out var imp) ? imp : 0.5;
        string tagsStr = record.Metadata.GetValueOrDefault("tags") ?? "";
        var tags = tagsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        DateTimeOffset occurredAt = DateTimeOffset.TryParse(
            record.Metadata.GetValueOrDefault("occurredAtUtc"), out var parsed)
            ? parsed : record.CreatedAtUtc;

        // Extract properties (metadata minus our control keys)
        var controlKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "entryType", "category", "importance", "tags", "occurredAtUtc" };
        var properties = record.Metadata
            .Where(kv => !controlKeys.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        return new OrganizationalMemoryEntry(
            EntryId: record.Id,
            EntryType: entryType,
            Category: category,
            Summary: record.Content,
            Properties: properties,
            Tags: tags,
            Importance: importance,
            OccurredAtUtc: occurredAt,
            StoredAtUtc: record.CreatedAtUtc);
    }

    private static IReadOnlyList<float> GenerateSimpleEmbedding(string text)
    {
        // Simple bag-of-character-trigrams embedding for term matching.
        // Real production would call an embedding model.
        var embedding = new float[128];
        string normalized = text.ToLowerInvariant();

        for (int i = 0; i <= normalized.Length - 3; i++)
        {
            int hash = normalized.Substring(i, 3).GetHashCode(StringComparison.Ordinal);
            int index = ((hash % embedding.Length) + embedding.Length) % embedding.Length;
            embedding[index] += 1.0f;
        }

        // Normalize
        float magnitude = MathF.Sqrt(embedding.Sum(v => v * v));
        if (magnitude > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
                embedding[i] /= magnitude;
        }

        return embedding;
    }

    private static HashSet<string> ExtractTerms(string text)
    {
        return text
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static double ComputeTermMatchScore(
        string summary, IReadOnlyList<string> tags, IReadOnlyCollection<string> queryTerms)
    {
        if (queryTerms.Count == 0) return 0;

        string combined = $"{summary} {string.Join(" ", tags)}".ToLowerInvariant();
        int matches = queryTerms.Count(term => combined.Contains(term, StringComparison.OrdinalIgnoreCase));
        return (double)matches / queryTerms.Count;
    }

    private static string TruncateForDisplay(string text)
    {
        return text.Length <= 80 ? text : text[..77] + "...";
    }
}

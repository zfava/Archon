using System.Collections.Concurrent;
using System.Diagnostics;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Knowledge;
using ArchonAI.Core.Models.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Memory;

public sealed class MemoryRetrievalOptimizer : IMemoryRetrievalOptimizer
{
    private readonly IMemoryStore _memoryStore;
    private readonly IKnowledgeGraphStore _knowledgeGraphStore;
    private readonly MemoryRetrievalOptions _options;
    private readonly ILogger<MemoryRetrievalOptimizer> _logger;

    // Scope-partitioned index: scope -> (recordId -> embedding)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, float[]>> _scopeIndices = new();

    // Track record counts per scope for auto-rebuild
    private readonly ConcurrentDictionary<string, int> _scopeRecordCounts = new();

    private long _retrievalQueries;
    private long _graphEnrichedQueries;
    private long _indexedRecords;

    public MemoryRetrievalOptimizer(
        IMemoryStore memoryStore,
        IKnowledgeGraphStore knowledgeGraphStore,
        IOptions<MemoryRetrievalOptions> options,
        ILogger<MemoryRetrievalOptimizer> logger)
    {
        _memoryStore = memoryStore;
        _knowledgeGraphStore = knowledgeGraphStore;
        _options = options.Value;
        _logger = logger;
    }

    public async global::System.Threading.Tasks.Task<RetrievalResult> SearchAsync(
        string scope, IReadOnlyList<float> queryEmbedding, int topK = 10,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        using var activity = Telemetry.ActivitySource.StartActivity("memory.retrieval.optimized");
        activity?.SetTag("scope", scope);
        activity?.SetTag("topK", topK);

        Interlocked.Increment(ref _retrievalQueries);
        Telemetry.MemoryRetrievalQueries.Add(1);

        topK = Math.Clamp(topK, 1, _options.MaxTopK);
        float[] query = queryEmbedding.ToArray();

        // Get all records in scope (including from the underlying store)
        var records = await _memoryStore.QueryByScopeAsync(scope, cancellationToken);
        var activeRecords = records
            .Where(r => !r.ExpiresAtUtc.HasValue || r.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .ToList();

        // Also do a standard semantic search to get embedding-based matches
        var semanticResults = await _memoryStore.SemanticSearchAsync(scope, queryEmbedding, topK * 3, cancellationToken);

        // Build a lookup of semantic results by ID for scoring
        var semanticScores = new Dictionary<Guid, double>();
        var semanticList = semanticResults.ToList();
        for (int i = 0; i < semanticList.Count; i++)
        {
            // Score decays by position (first result = 1.0, last = lower)
            double positionScore = 1.0 - ((double)i / Math.Max(1, semanticList.Count));
            semanticScores[semanticList[i].Id] = positionScore;
        }

        // Query knowledge graph for context enrichment
        var graphRelatedNodeIds = await GetGraphRelatedNodeIdsAsync(scope, cancellationToken);
        bool hasGraphContext = graphRelatedNodeIds.Count > 0;
        if (hasGraphContext)
        {
            Interlocked.Increment(ref _graphEnrichedQueries);
            Telemetry.MemoryGraphEnrichedQueries.Add(1);
        }

        // Score all candidate records
        var now = DateTimeOffset.UtcNow;
        var scored = new List<ScoredMemoryRecord>();

        foreach (var record in activeRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Relevance score from semantic search
            double relevance = semanticScores.GetValueOrDefault(record.Id, 0.0);

            // Recency boost: exponential decay based on age
            double ageHours = Math.Max(0, (now - record.CreatedAtUtc).TotalHours);
            double recencyBoost = Math.Exp(-_options.RecencyDecayFactor * ageHours);

            // Knowledge graph boost: records related to graph entities get a boost
            double graphBoost = 0.0;
            if (hasGraphContext && record.Metadata.Any(kv =>
                graphRelatedNodeIds.Contains(kv.Value, StringComparer.OrdinalIgnoreCase)))
            {
                graphBoost = 1.0;
            }

            // Weighted final score
            double finalScore =
                (relevance * _options.RelevanceWeight) +
                (recencyBoost * _options.RecencyBoostWeight) +
                (graphBoost * _options.GraphBoostWeight);

            scored.Add(new ScoredMemoryRecord(
                Record: record,
                RelevanceScore: relevance,
                RecencyBoost: recencyBoost,
                GraphBoost: hasGraphContext ? graphBoost : null,
                FinalScore: finalScore));
        }

        // Sort by final score descending, take topK
        var topResults = scored
            .OrderByDescending(s => s.FinalScore)
            .Take(topK)
            .ToList();

        sw.Stop();
        string strategy = hasGraphContext ? "semantic+recency+graph" : "semantic+recency";

        _logger.LogDebug(
            "Memory retrieval for scope '{Scope}': {Candidates} candidates, {Results} results in {Duration}ms (strategy: {Strategy})",
            scope, activeRecords.Count, topResults.Count, sw.Elapsed.TotalMilliseconds, strategy);

        return new RetrievalResult(
            Records: topResults,
            TotalCandidates: activeRecords.Count,
            SearchDurationMs: sw.Elapsed.TotalMilliseconds,
            Strategy: strategy);
    }

    public async global::System.Threading.Tasks.Task RebuildIndexAsync(
        string scope, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("memory.index.rebuild");
        activity?.SetTag("scope", scope);

        var records = await _memoryStore.QueryByScopeAsync(scope, cancellationToken);
        var activeRecords = records
            .Where(r => !r.ExpiresAtUtc.HasValue || r.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .ToList();

        var index = _scopeIndices.GetOrAdd(scope, _ => new ConcurrentDictionary<Guid, float[]>());
        index.Clear();

        // The actual embeddings are stored in the IMemoryStore implementation.
        // This index tracks which records are active per scope for fast lookup.
        foreach (var record in activeRecords)
        {
            index[record.Id] = Array.Empty<float>(); // Placeholder - embeddings are in the store
        }

        _scopeRecordCounts[scope] = activeRecords.Count;
        Interlocked.Exchange(ref _indexedRecords, _scopeIndices.Values.Sum(idx => idx.Count));

        _logger.LogInformation("Rebuilt memory index for scope '{Scope}': {Count} records indexed", scope, activeRecords.Count);
    }

    public MemoryServiceStatus GetStatus()
    {
        return new MemoryServiceStatus(
            CompressionRuns: 0, // Filled by compression engine, not this optimizer
            DuplicatesRemoved: 0,
            ClustersFormed: 0,
            SummariesCreated: 0,
            RetrievalQueries: Interlocked.Read(ref _retrievalQueries),
            GraphEnrichedQueries: Interlocked.Read(ref _graphEnrichedQueries),
            IndexedRecords: Interlocked.Read(ref _indexedRecords),
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<HashSet<string>> GetGraphRelatedNodeIdsAsync(
        string scope, CancellationToken cancellationToken)
    {
        var relatedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Extract entity IDs from the scope (e.g. "task:xxx", "agent:xxx", "workflow:xxx")
            if (scope.Contains(':'))
            {
                // Query the knowledge graph for related nodes
                var relatedNodes = await _knowledgeGraphStore.QueryRelatedNodesAsync(
                    scope, relationshipType: null, cancellationToken);

                foreach (var node in relatedNodes)
                {
                    relatedIds.Add(node.NodeId);
                    relatedIds.Add(node.DisplayName);

                    // Also add property values for matching
                    foreach (var prop in node.Properties)
                    {
                        if (!string.IsNullOrWhiteSpace(prop.Value))
                            relatedIds.Add(prop.Value);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query knowledge graph for scope '{Scope}'", scope);
        }

        return relatedIds;
    }
}

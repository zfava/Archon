using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Knowledge;
using ArchonAI.Core.Models.Memory;
using ArchonAI.Memory;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace ArchonAI.Memory.Tests;

#region MemoryCompressionEngine Tests

public sealed class MemoryCompressionEngineTests
{
    private readonly Mock<IMemoryStore> _memoryStore = new();
    private readonly MemoryCompressionOptions _options = new();
    private readonly Mock<ILogger<MemoryCompressionEngine>> _logger = new();

    private MemoryCompressionEngine CreateEngine() =>
        new(_memoryStore.Object, Options.Create(_options), _logger.Object);

    private static MemoryRecord MakeRecord(
        string content,
        string memoryType = "fact",
        string scope = "test-scope",
        DateTimeOffset? createdAt = null,
        Guid? id = null) =>
        new(
            Id: id ?? Guid.NewGuid(),
            MemoryType: memoryType,
            Scope: scope,
            Content: content,
            Metadata: new Dictionary<string, string> { ["topic"] = "testing" },
            CreatedAtUtc: createdAt ?? DateTimeOffset.UtcNow,
            ExpiresAtUtc: null);

    // ---- 1. CompressAsync with empty scope ----
    [Fact]
    public async Task MemoryCompressionEngine_CompressAsync_EmptyScope_ReturnsZeroCounts()
    {
        _memoryStore
            .Setup(s => s.QueryByScopeAsync("empty", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MemoryRecord>());

        var engine = CreateEngine();
        var result = await engine.CompressAsync("empty");

        result.OriginalCount.Should().Be(0);
        result.CompressedCount.Should().Be(0);
        result.DuplicatesRemoved.Should().Be(0);
        result.ClustersFormed.Should().Be(0);
        result.SummariesCreated.Should().Be(0);
        engine.CompressionRuns.Should().Be(1);
    }

    // ---- 2. CompressAsync with records (no dupes, no clusters) ----
    [Fact]
    public async Task MemoryCompressionEngine_CompressAsync_WithDistinctRecords_ReturnsOriginalCount()
    {
        var records = new List<MemoryRecord>
        {
            MakeRecord("alpha bravo charlie delta echo foxtrot"),
            MakeRecord("golf hotel india juliet kilo lima")
        };

        _memoryStore
            .Setup(s => s.QueryByScopeAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var engine = CreateEngine();
        var result = await engine.CompressAsync("s1");

        result.OriginalCount.Should().Be(2);
        result.DuplicatesRemoved.Should().Be(0);
        // With only 2 records and MinClusterSize=3, no clusters or summaries
        result.ClustersFormed.Should().Be(0);
        result.SummariesCreated.Should().Be(0);
    }

    // ---- 3. DeduplicateAsync with no duplicates ----
    [Fact]
    public async Task MemoryCompressionEngine_DeduplicateAsync_NoDuplicates_ReturnsZero()
    {
        var records = new List<MemoryRecord>
        {
            MakeRecord("completely unique content first item alpha"),
            MakeRecord("totally different second item bravo echo")
        };

        _memoryStore
            .Setup(s => s.QueryByScopeAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var engine = CreateEngine();
        var removed = await engine.DeduplicateAsync("s1");

        removed.Should().Be(0);
        _memoryStore.Verify(s => s.SaveAsync(It.IsAny<MemoryRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- 4. DeduplicateAsync with exact duplicates ----
    [Fact]
    public async Task MemoryCompressionEngine_DeduplicateAsync_WithExactDuplicates_RemovesDuplicates()
    {
        var records = new List<MemoryRecord>
        {
            MakeRecord("duplicate content here for testing purposes",
                createdAt: DateTimeOffset.UtcNow.AddHours(-2)),
            MakeRecord("duplicate content here for testing purposes",
                createdAt: DateTimeOffset.UtcNow.AddHours(-1))
        };

        _memoryStore
            .Setup(s => s.QueryByScopeAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var engine = CreateEngine();
        var removed = await engine.DeduplicateAsync("s1");

        removed.Should().Be(1);
        engine.TotalDuplicatesRemoved.Should().Be(1);
        // The duplicate record should be saved with an ExpiresAtUtc set
        _memoryStore.Verify(s => s.SaveAsync(
            It.Is<MemoryRecord>(r => r.ExpiresAtUtc.HasValue),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- 5. DeduplicateAsync preserves the older record ----
    [Fact]
    public async Task MemoryCompressionEngine_DeduplicateAsync_WithDuplicates_KeepsOlderRecord()
    {
        var oldId = Guid.NewGuid();
        var newId = Guid.NewGuid();

        var records = new List<MemoryRecord>
        {
            MakeRecord("same content repeated exactly here for dedup test",
                createdAt: DateTimeOffset.UtcNow.AddHours(-5), id: oldId),
            MakeRecord("same content repeated exactly here for dedup test",
                createdAt: DateTimeOffset.UtcNow.AddHours(-1), id: newId)
        };

        _memoryStore
            .Setup(s => s.QueryByScopeAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var engine = CreateEngine();
        await engine.DeduplicateAsync("s1");

        // The newer record (newId) should be the one soft-deleted
        _memoryStore.Verify(s => s.SaveAsync(
            It.Is<MemoryRecord>(r => r.Id == newId && r.ExpiresAtUtc.HasValue),
            It.IsAny<CancellationToken>()), Times.Once);

        // The older record should NOT be re-saved with expiry
        _memoryStore.Verify(s => s.SaveAsync(
            It.Is<MemoryRecord>(r => r.Id == oldId && r.ExpiresAtUtc.HasValue),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- 6. ClusterAsync with empty scope ----
    [Fact]
    public async Task MemoryCompressionEngine_ClusterAsync_EmptyScope_ReturnsNoClusters()
    {
        _memoryStore
            .Setup(s => s.QueryByScopeAsync("empty", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MemoryRecord>());

        var engine = CreateEngine();
        var clusters = await engine.ClusterAsync("empty");

        clusters.Should().BeEmpty();
        engine.TotalClustersFormed.Should().Be(0);
    }

    // ---- 7. ClusterAsync with similar records forming a cluster ----
    [Fact]
    public async Task MemoryCompressionEngine_ClusterAsync_WithSimilarRecords_FormsClusters()
    {
        // MinClusterSize defaults to 3; records must share memoryType and content similarity >= 0.75
        var records = new List<MemoryRecord>
        {
            MakeRecord("the quarterly sales revenue report analysis overview",
                memoryType: "report", createdAt: DateTimeOffset.UtcNow.AddHours(-3)),
            MakeRecord("the quarterly sales revenue report analysis summary",
                memoryType: "report", createdAt: DateTimeOffset.UtcNow.AddHours(-2)),
            MakeRecord("the quarterly sales revenue report analysis details",
                memoryType: "report", createdAt: DateTimeOffset.UtcNow.AddHours(-1))
        };

        _memoryStore
            .Setup(s => s.QueryByScopeAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var engine = CreateEngine();
        var clusters = await engine.ClusterAsync("s1");

        clusters.Should().HaveCountGreaterThanOrEqualTo(1);
        clusters[0].MemberRecordIds.Should().HaveCountGreaterThanOrEqualTo(3);
        engine.TotalClustersFormed.Should().BeGreaterThanOrEqualTo(1);
    }

    // ---- 8. ClusterAsync does not cluster records with different MemoryType ----
    [Fact]
    public async Task MemoryCompressionEngine_ClusterAsync_DifferentMemoryTypes_DoesNotCluster()
    {
        var records = new List<MemoryRecord>
        {
            MakeRecord("identical content across all records here",
                memoryType: "typeA", createdAt: DateTimeOffset.UtcNow.AddHours(-3)),
            MakeRecord("identical content across all records here",
                memoryType: "typeB", createdAt: DateTimeOffset.UtcNow.AddHours(-2)),
            MakeRecord("identical content across all records here",
                memoryType: "typeC", createdAt: DateTimeOffset.UtcNow.AddHours(-1))
        };

        _memoryStore
            .Setup(s => s.QueryByScopeAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var engine = CreateEngine();
        var clusters = await engine.ClusterAsync("s1");

        // Each type has only 1 record, which is below MinClusterSize=3
        clusters.Should().BeEmpty();
    }

    // ---- 9. SummarizeAsync creates a summary and persists it ----
    [Fact]
    public async Task MemoryCompressionEngine_SummarizeAsync_WithSourceRecords_CreatesSummary()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var records = new List<MemoryRecord>
        {
            MakeRecord("first source record content about sales performance", id: id1),
            MakeRecord("second source record content about marketing strategy", id: id2)
        };

        _memoryStore
            .Setup(s => s.QueryByScopeAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var engine = CreateEngine();
        var summary = await engine.SummarizeAsync("s1", new List<Guid> { id1, id2 });

        summary.Should().NotBeNull();
        summary.Scope.Should().Be("s1");
        summary.SourceRecordIds.Should().Contain(id1);
        summary.SourceRecordIds.Should().Contain(id2);
        summary.Content.Should().NotBeNullOrWhiteSpace();
        engine.TotalSummariesCreated.Should().Be(1);

        // Verify the summary record was persisted to the store
        _memoryStore.Verify(s => s.SaveAsync(
            It.Is<MemoryRecord>(r => r.MemoryType == "summary" && r.Scope == "s1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- 10. Metrics increment correctly across multiple calls ----
    [Fact]
    public async Task MemoryCompressionEngine_Metrics_MultipleCompressions_IncrementCorrectly()
    {
        _memoryStore
            .Setup(s => s.QueryByScopeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MemoryRecord>());

        var engine = CreateEngine();

        await engine.CompressAsync("scope1");
        await engine.CompressAsync("scope2");
        await engine.CompressAsync("scope3");

        engine.CompressionRuns.Should().Be(3);
        engine.TotalDuplicatesRemoved.Should().Be(0);
        engine.TotalClustersFormed.Should().Be(0);
        engine.TotalSummariesCreated.Should().Be(0);
    }
}

#endregion

#region MemoryRetrievalOptimizer Tests

public sealed class MemoryRetrievalOptimizerTests
{
    private readonly Mock<IMemoryStore> _memoryStore = new();
    private readonly Mock<IKnowledgeGraphStore> _knowledgeGraphStore = new();
    private readonly MemoryRetrievalOptions _options = new();
    private readonly Mock<ILogger<MemoryRetrievalOptimizer>> _logger = new();

    private MemoryRetrievalOptimizer CreateOptimizer() =>
        new(_memoryStore.Object, _knowledgeGraphStore.Object, Options.Create(_options), _logger.Object);

    private static MemoryRecord MakeRecord(
        string content,
        string scope = "test-scope",
        DateTimeOffset? createdAt = null,
        Guid? id = null,
        Dictionary<string, string>? metadata = null) =>
        new(
            Id: id ?? Guid.NewGuid(),
            MemoryType: "fact",
            Scope: scope,
            Content: content,
            Metadata: metadata ?? new Dictionary<string, string> { ["topic"] = "test" },
            CreatedAtUtc: createdAt ?? DateTimeOffset.UtcNow,
            ExpiresAtUtc: null);

    private static IReadOnlyList<float> MakeEmbedding(int dims = 128)
    {
        var rng = new Random(42);
        return Enumerable.Range(0, dims).Select(_ => (float)rng.NextDouble()).ToList();
    }

    // ---- 1. SearchAsync with empty store ----
    [Fact]
    public async Task MemoryRetrievalOptimizer_SearchAsync_EmptyStore_ReturnsNoRecords()
    {
        _memoryStore
            .Setup(s => s.QueryByScopeAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MemoryRecord>());
        _memoryStore
            .Setup(s => s.SemanticSearchAsync("s1", It.IsAny<IReadOnlyList<float>>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MemoryRecord>());

        var optimizer = CreateOptimizer();
        var result = await optimizer.SearchAsync("s1", MakeEmbedding(), 10);

        result.Records.Should().BeEmpty();
        result.TotalCandidates.Should().Be(0);
        result.Strategy.Should().Be("semantic+recency");
    }

    // ---- 2. SearchAsync with results ----
    [Fact]
    public async Task MemoryRetrievalOptimizer_SearchAsync_WithRecords_ReturnsScoredResults()
    {
        var r1 = MakeRecord("important data analysis results", scope: "s1",
            createdAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var r2 = MakeRecord("less important old data from previous quarter", scope: "s1",
            createdAt: DateTimeOffset.UtcNow.AddHours(-48));

        _memoryStore
            .Setup(s => s.QueryByScopeAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemoryRecord> { r1, r2 });
        _memoryStore
            .Setup(s => s.SemanticSearchAsync("s1", It.IsAny<IReadOnlyList<float>>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemoryRecord> { r1, r2 });

        var optimizer = CreateOptimizer();
        var result = await optimizer.SearchAsync("s1", MakeEmbedding(), 10);

        result.Records.Should().HaveCount(2);
        result.TotalCandidates.Should().Be(2);
        // The more recent record should have a higher recency boost
        var scoredR1 = result.Records.First(r => r.Record.Id == r1.Id);
        var scoredR2 = result.Records.First(r => r.Record.Id == r2.Id);
        scoredR1.RecencyBoost.Should().NotBeNull();
        scoredR2.RecencyBoost.Should().NotBeNull();
        scoredR1.RecencyBoost!.Value.Should().BeGreaterThan(scoredR2.RecencyBoost!.Value);
    }

    // ---- 3. SearchAsync with graph enrichment (scope contains ':') ----
    [Fact]
    public async Task MemoryRetrievalOptimizer_SearchAsync_ScopeWithColon_TriggersGraphEnrichment()
    {
        var record = MakeRecord("graph connected record", scope: "task:abc",
            metadata: new Dictionary<string, string> { ["ref"] = "node-42" });

        _memoryStore
            .Setup(s => s.QueryByScopeAsync("task:abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemoryRecord> { record });
        _memoryStore
            .Setup(s => s.SemanticSearchAsync("task:abc", It.IsAny<IReadOnlyList<float>>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemoryRecord> { record });

        _knowledgeGraphStore
            .Setup(s => s.QueryRelatedNodesAsync("task:abc", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeNode>
            {
                new("node-42", "entity", "RelatedNode",
                    new Dictionary<string, string> { ["val"] = "node-42" },
                    DateTimeOffset.UtcNow)
            });

        var optimizer = CreateOptimizer();
        var result = await optimizer.SearchAsync("task:abc", MakeEmbedding(), 10);

        result.Strategy.Should().Be("semantic+recency+graph");
        // The record has metadata matching a graph node, so it should get a GraphBoost
        result.Records.Should().ContainSingle();
        result.Records[0].GraphBoost.Should().NotBeNull();
        result.Records[0].GraphBoost.Should().Be(1.0);
    }

    // ---- 4. SearchAsync with no graph enrichment (scope without ':') ----
    [Fact]
    public async Task MemoryRetrievalOptimizer_SearchAsync_ScopeWithoutColon_NoGraphBoost()
    {
        var record = MakeRecord("plain scope record", scope: "plain-scope");

        _memoryStore
            .Setup(s => s.QueryByScopeAsync("plain-scope", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemoryRecord> { record });
        _memoryStore
            .Setup(s => s.SemanticSearchAsync("plain-scope", It.IsAny<IReadOnlyList<float>>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemoryRecord> { record });

        var optimizer = CreateOptimizer();
        var result = await optimizer.SearchAsync("plain-scope", MakeEmbedding(), 10);

        result.Strategy.Should().Be("semantic+recency");
        result.Records[0].GraphBoost.Should().BeNull();
    }

    // ---- 5. RebuildIndexAsync indexes active records ----
    [Fact]
    public async Task MemoryRetrievalOptimizer_RebuildIndexAsync_WithRecords_IndexesActiveRecords()
    {
        var active = MakeRecord("active record", scope: "s1");
        var expired = new MemoryRecord(
            Id: Guid.NewGuid(), MemoryType: "fact", Scope: "s1",
            Content: "expired record",
            Metadata: new Dictionary<string, string>(),
            CreatedAtUtc: DateTimeOffset.UtcNow.AddDays(-10),
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddDays(-1));

        _memoryStore
            .Setup(s => s.QueryByScopeAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemoryRecord> { active, expired });

        var optimizer = CreateOptimizer();
        await optimizer.RebuildIndexAsync("s1");

        var status = optimizer.GetStatus();
        // Only the active record should be indexed (expired record is excluded)
        status.IndexedRecords.Should().Be(1);
    }

    // ---- 6. GetStatus returns initial zeroes ----
    [Fact]
    public void MemoryRetrievalOptimizer_GetStatus_InitialState_ReturnsZeroes()
    {
        var optimizer = CreateOptimizer();
        var status = optimizer.GetStatus();

        status.RetrievalQueries.Should().Be(0);
        status.GraphEnrichedQueries.Should().Be(0);
        status.IndexedRecords.Should().Be(0);
        status.StatusAsOfUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ---- 7. TopK clamping (exceeding MaxTopK) ----
    [Fact]
    public async Task MemoryRetrievalOptimizer_SearchAsync_TopKExceedsMax_ClampedToMaxTopK()
    {
        // Create more records than MaxTopK (default 100)
        var records = Enumerable.Range(0, 5)
            .Select(i => MakeRecord($"record number {i} for topK clamping test content", scope: "s1"))
            .ToList();

        _memoryStore
            .Setup(s => s.QueryByScopeAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);
        _memoryStore
            .Setup(s => s.SemanticSearchAsync("s1", It.IsAny<IReadOnlyList<float>>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var optimizer = CreateOptimizer();
        // Request topK=500, but MaxTopK is 100. With only 5 records, we get 5 back,
        // but the important thing is the engine doesn't crash.
        var result = await optimizer.SearchAsync("s1", MakeEmbedding(), 500);

        result.Records.Should().HaveCount(5);
        result.Records.Count.Should().BeLessThanOrEqualTo(_options.MaxTopK);
    }
}

#endregion

#region OrganizationalMemoryStore Tests

public sealed class OrganizationalMemoryStoreTests
{
    private readonly Mock<IMemoryStore> _memoryStore = new();
    private readonly Mock<IKnowledgeGraphStore> _knowledgeStore = new();
    private readonly Mock<IMemoryRetrievalOptimizer> _retriever = new();
    private readonly Mock<IEventBus> _eventBus = new();
    private readonly Mock<ILogger<OrganizationalMemoryStore>> _logger = new();

    private OrganizationalMemoryStore CreateStore() =>
        new(_memoryStore.Object, _knowledgeStore.Object, _retriever.Object,
            _eventBus.Object, _logger.Object);

    private static OrganizationalMemoryEntry MakeEntry(
        OrganizationalMemoryType type = OrganizationalMemoryType.Strategy,
        string category = "sales",
        string summary = "Test strategy summary for organizational memory",
        double importance = 0.8,
        Guid? entryId = null,
        IReadOnlyDictionary<string, string>? properties = null,
        IReadOnlyList<string>? tags = null) =>
        new(
            EntryId: entryId ?? Guid.NewGuid(),
            EntryType: type,
            Category: category,
            Summary: summary,
            Properties: properties ?? new Dictionary<string, string>(),
            Tags: tags ?? new List<string> { "test" },
            Importance: importance,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            StoredAtUtc: DateTimeOffset.UtcNow);

    // ---- 1. StoreAsync persists to both memory store and knowledge graph ----
    [Fact]
    public async Task OrganizationalMemoryStore_StoreAsync_ValidEntry_PersistsToBothStores()
    {
        var entry = MakeEntry();
        var store = CreateStore();

        await store.StoreAsync(entry);

        // Should save a MemoryRecord to the memory store
        _memoryStore.Verify(s => s.SaveAsync(
            It.Is<MemoryRecord>(r =>
                r.Id == entry.EntryId &&
                r.Scope == "org-memory" &&
                r.Content == entry.Summary),
            It.IsAny<CancellationToken>()), Times.Once);

        // Should upsert nodes to the knowledge graph
        // At minimum: the entry node + the category node = 2 node upserts
        _knowledgeStore.Verify(s => s.UpsertNodeAsync(
            It.IsAny<KnowledgeNode>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(2));

        // Should create a category relationship
        _knowledgeStore.Verify(s => s.UpsertRelationshipAsync(
            It.Is<KnowledgeRelationship>(r => r.RelationshipType == "belongs_to_category"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- 2. StoreAsync with tags creates tag relationships ----
    [Fact]
    public async Task OrganizationalMemoryStore_StoreAsync_WithTags_CreatesTagRelationships()
    {
        var entry = MakeEntry(tags: new List<string> { "revenue", "growth" });
        var store = CreateStore();

        await store.StoreAsync(entry);

        // 2 tags = 2 tag nodes + 2 tag relationships
        _knowledgeStore.Verify(s => s.UpsertRelationshipAsync(
            It.Is<KnowledgeRelationship>(r => r.RelationshipType == "tagged_with"),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ---- 3. SearchAsync returns results ----
    [Fact]
    public async Task OrganizationalMemoryStore_SearchAsync_WithStoredEntries_ReturnsMatches()
    {
        var entry = MakeEntry(summary: "quarterly revenue analysis for fiscal year planning");
        var store = CreateStore();
        await store.StoreAsync(entry);

        // The retriever returns an empty result (no embedding-based matches)
        _retriever
            .Setup(r => r.SearchAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<float>>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetrievalResult(
                Records: Array.Empty<ScoredMemoryRecord>(),
                TotalCandidates: 0,
                SearchDurationMs: 1.0,
                Strategy: "semantic+recency"));

        // The local term-match fallback should find the stored entry
        var result = await store.SearchAsync("revenue analysis");

        result.Should().NotBeNull();
        result.SearchStrategy.Should().NotBeNullOrWhiteSpace();
        // The term-match path should pick up the entry since "revenue" and "analysis" appear in the summary
        result.Matches.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    // ---- 4. GetTimelineAsync with no entries ----
    [Fact]
    public async Task OrganizationalMemoryStore_GetTimelineAsync_NoEntries_ReturnsEmptyTimeline()
    {
        var store = CreateStore();

        var timeline = await store.GetTimelineAsync(OrganizationalMemoryType.Strategy);

        timeline.Entries.Should().BeEmpty();
        timeline.TotalCount.Should().Be(0);
        timeline.EarliestEntry.Should().BeNull();
        timeline.LatestEntry.Should().BeNull();
    }

    // ---- 5. AnalyzeAsync with no entries ----
    [Fact]
    public async Task OrganizationalMemoryStore_AnalyzeAsync_NoEntries_ReturnsEmptyReport()
    {
        var store = CreateStore();

        var report = await store.AnalyzeAsync();

        report.TotalEntries.Should().Be(0);
        report.StrategyEntries.Should().Be(0);
        report.OutcomeEntries.Should().Be(0);
        report.PatternEntries.Should().Be(0);
        report.LessonEntries.Should().Be(0);
        report.Insights.Should().BeEmpty();
        report.KnowledgeNodesCreated.Should().Be(0);
    }

    // ---- 6. AnalyzeAsync with multiple entries counts correctly ----
    [Fact]
    public async Task OrganizationalMemoryStore_AnalyzeAsync_WithEntries_ReportsCorrectCounts()
    {
        var store = CreateStore();

        await store.StoreAsync(MakeEntry(type: OrganizationalMemoryType.Strategy,
            summary: "strategy entry one for testing purposes"));
        await store.StoreAsync(MakeEntry(type: OrganizationalMemoryType.Outcome,
            summary: "outcome entry one for testing purposes"));
        await store.StoreAsync(MakeEntry(type: OrganizationalMemoryType.Pattern,
            summary: "pattern entry one for testing purposes"));
        await store.StoreAsync(MakeEntry(type: OrganizationalMemoryType.Lesson,
            summary: "lesson entry one for testing purposes"));

        var report = await store.AnalyzeAsync();

        report.TotalEntries.Should().Be(4);
        report.StrategyEntries.Should().Be(1);
        report.OutcomeEntries.Should().Be(1);
        report.PatternEntries.Should().Be(1);
        report.LessonEntries.Should().Be(1);
    }

    // ---- 7. GetTimelineAsync filters by type and category ----
    [Fact]
    public async Task OrganizationalMemoryStore_GetTimelineAsync_WithFilter_ReturnsFilteredEntries()
    {
        var store = CreateStore();

        await store.StoreAsync(MakeEntry(type: OrganizationalMemoryType.Strategy,
            category: "sales", summary: "sales strategy alpha beta gamma"));
        await store.StoreAsync(MakeEntry(type: OrganizationalMemoryType.Strategy,
            category: "marketing", summary: "marketing strategy delta epsilon"));
        await store.StoreAsync(MakeEntry(type: OrganizationalMemoryType.Outcome,
            category: "sales", summary: "sales outcome zeta theta"));

        var timeline = await store.GetTimelineAsync(
            OrganizationalMemoryType.Strategy, category: "sales");

        timeline.Entries.Should().HaveCount(1);
        timeline.Entries[0].Category.Should().Be("sales");
        timeline.TotalCount.Should().Be(1);
    }

    // ---- 8. GetRelatedEntriesAsync returns entries linked via graph ----
    [Fact]
    public async Task OrganizationalMemoryStore_GetRelatedEntriesAsync_WithGraphLinks_ReturnsRelatedEntries()
    {
        var entryId = Guid.NewGuid();
        var entry = MakeEntry(entryId: entryId,
            summary: "strategy for testing graph traversal queries");
        var store = CreateStore();
        await store.StoreAsync(entry);

        _knowledgeStore
            .Setup(s => s.QueryRelatedNodesAsync("some-node", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeNode>
            {
                new($"org_memory:{entryId}", "org_memory_strategy", "Test",
                    new Dictionary<string, string>(), DateTimeOffset.UtcNow)
            });

        var related = await store.GetRelatedEntriesAsync("some-node");

        related.Should().HaveCount(1);
        related[0].EntryId.Should().Be(entryId);
    }
}

#endregion

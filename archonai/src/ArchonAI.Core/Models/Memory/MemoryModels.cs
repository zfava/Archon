namespace ArchonAI.Core.Models.Memory;

public sealed record CompressionResult(
    int OriginalCount,
    int CompressedCount,
    int DuplicatesRemoved,
    int ClustersFormed,
    int SummariesCreated,
    DateTimeOffset CompressedAtUtc);

public sealed record MemoryCluster(
    Guid Id,
    string Scope,
    string Topic,
    IReadOnlyList<Guid> MemberRecordIds,
    string Summary,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAtUtc);

public sealed record MemorySummary(
    Guid Id,
    string Scope,
    string OriginalMemoryType,
    IReadOnlyList<Guid> SourceRecordIds,
    string Content,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAtUtc);

public sealed record RetrievalResult(
    IReadOnlyList<ScoredMemoryRecord> Records,
    int TotalCandidates,
    double SearchDurationMs,
    string Strategy);

public sealed record ScoredMemoryRecord(
    MemoryRecord Record,
    double RelevanceScore,
    double? RecencyBoost,
    double? GraphBoost,
    double FinalScore);

public sealed record MemoryCompressionOptions
{
    public const string SectionName = "MemoryCompression";
    public double DuplicateSimilarityThreshold { get; set; } = 0.95;
    public double ClusterSimilarityThreshold { get; set; } = 0.75;
    public int MinClusterSize { get; set; } = 3;
    public int MaxClusterSize { get; set; } = 50;
    public int MaxSummarySourceRecords { get; set; } = 20;
    public int CompressionAgeThresholdHours { get; set; } = 24;
    public int MaxRecordsPerCompressionRun { get; set; } = 1000;
}

public sealed record MemoryRetrievalOptions
{
    public const string SectionName = "MemoryRetrieval";
    public double RecencyDecayFactor { get; set; } = 0.1;
    public double RecencyBoostWeight { get; set; } = 0.15;
    public double GraphBoostWeight { get; set; } = 0.10;
    public double RelevanceWeight { get; set; } = 0.75;
    public int DefaultTopK { get; set; } = 10;
    public int MaxTopK { get; set; } = 100;
    public int IndexRebuildThreshold { get; set; } = 500;
}

public sealed record MemoryServiceStatus(
    long CompressionRuns,
    long DuplicatesRemoved,
    long ClustersFormed,
    long SummariesCreated,
    long RetrievalQueries,
    long GraphEnrichedQueries,
    long IndexedRecords,
    DateTimeOffset StatusAsOfUtc);

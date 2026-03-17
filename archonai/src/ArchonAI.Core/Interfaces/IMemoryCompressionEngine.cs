using ArchonAI.Core.Models.Memory;

namespace ArchonAI.Core.Interfaces;

public interface IMemoryCompressionEngine
{
    /// <summary>
    /// Compress memories in the given scope: summarize old records, cluster related records,
    /// and remove duplicates.
    /// </summary>
    global::System.Threading.Tasks.Task<CompressionResult> CompressAsync(
        string scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove duplicate memory records based on content similarity.
    /// </summary>
    global::System.Threading.Tasks.Task<int> DeduplicateAsync(
        string scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cluster related memory records by topic similarity.
    /// </summary>
    global::System.Threading.Tasks.Task<IReadOnlyList<MemoryCluster>> ClusterAsync(
        string scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Summarize a batch of memory records into a single condensed record.
    /// </summary>
    global::System.Threading.Tasks.Task<MemorySummary> SummarizeAsync(
        string scope,
        IReadOnlyList<Guid> sourceRecordIds,
        CancellationToken cancellationToken = default);
}

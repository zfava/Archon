using ArchonAI.Core.Models.Memory;

namespace ArchonAI.Core.Interfaces;

public interface IMemoryRetrievalOptimizer
{
    /// <summary>
    /// Optimized semantic search with recency boosting and knowledge graph enrichment.
    /// </summary>
    global::System.Threading.Tasks.Task<RetrievalResult> SearchAsync(
        string scope,
        IReadOnlyList<float> queryEmbedding,
        int topK = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuild the in-memory index for the given scope to improve search performance.
    /// </summary>
    global::System.Threading.Tasks.Task RebuildIndexAsync(
        string scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns current service status and metrics.
    /// </summary>
    MemoryServiceStatus GetStatus();
}

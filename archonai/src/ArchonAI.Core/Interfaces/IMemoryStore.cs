using ArchonAI.Core.Models;

namespace ArchonAI.Core.Interfaces;

public interface IMemoryStore
{
    global::System.Threading.Tasks.Task SaveAsync(MemoryRecord record, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task SaveEmbeddingAsync(
        Guid memoryRecordId,
        IReadOnlyList<float> embedding,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<MemoryRecord>> QueryByScopeAsync(
        string scope,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<MemoryRecord>> SemanticSearchAsync(
        string scope,
        IReadOnlyList<float> queryEmbedding,
        int topK = 5,
        CancellationToken cancellationToken = default);
}

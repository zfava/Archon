using ArchonAI.Core.Models;

namespace ArchonAI.Infrastructure.Memory;

public interface IMemoryRecordRepository
{
    global::System.Threading.Tasks.Task SaveAsync(MemoryRecord record, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task SaveEmbeddingAsync(Guid memoryRecordId, IReadOnlyList<float> embedding, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<MemoryRecord>> QueryByScopeAsync(string scope, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<MemoryRecord>> SemanticSearchAsync(string scope, IReadOnlyList<float> queryEmbedding, int topK, CancellationToken cancellationToken = default);
}

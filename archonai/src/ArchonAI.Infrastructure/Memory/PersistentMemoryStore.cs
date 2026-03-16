using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Trace;

namespace ArchonAI.Infrastructure.Memory;

public sealed class PersistentMemoryStore : IMemoryStore
{
    private readonly IMemoryRecordRepository _repository;
    private readonly ITraceStore _traceStore;

    public PersistentMemoryStore(IMemoryRecordRepository repository, ITraceStore traceStore)
    {
        _repository = repository;
        _traceStore = traceStore;
    }

    public global::System.Threading.Tasks.Task SaveAsync(MemoryRecord record, CancellationToken cancellationToken = default)
        => _repository.SaveAsync(record, cancellationToken);

    public global::System.Threading.Tasks.Task SaveEmbeddingAsync(Guid memoryRecordId, IReadOnlyList<float> embedding, CancellationToken cancellationToken = default)
        => _repository.SaveEmbeddingAsync(memoryRecordId, embedding, cancellationToken);

    public async global::System.Threading.Tasks.Task<IReadOnlyList<MemoryRecord>> QueryByScopeAsync(string scope, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MemoryRecord> result = await _repository.QueryByScopeAsync(scope, cancellationToken);
        await _traceStore.RecordAsync(new TraceEntry(
            Id: Guid.NewGuid(),
            Scope: scope,
            Category: "memory-retrieval",
            Message: "Persistent memory query by scope executed.",
            Metadata: new Dictionary<string, string>
            {
                ["scope"] = scope,
                ["resultCount"] = result.Count.ToString()
            },
            RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        return result;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<MemoryRecord>> SemanticSearchAsync(string scope, IReadOnlyList<float> queryEmbedding, int topK = 5, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MemoryRecord> result = await _repository.SemanticSearchAsync(scope, queryEmbedding, topK, cancellationToken);
        await _traceStore.RecordAsync(new TraceEntry(
            Id: Guid.NewGuid(),
            Scope: scope,
            Category: "memory-retrieval",
            Message: "Persistent semantic search executed.",
            Metadata: new Dictionary<string, string>
            {
                ["scope"] = scope,
                ["topK"] = topK.ToString(),
                ["resultCount"] = result.Count.ToString()
            },
            RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        return result;
    }
}

using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Trace;

namespace ArchonAI.Infrastructure.Services;

public sealed class InMemoryStore : IMemoryStore
{
    private readonly ConcurrentDictionary<Guid, MemoryRecord> _records = new();
    private readonly ConcurrentDictionary<Guid, float[]> _embeddings = new();
    private readonly ITraceStore _traceStore;

    public InMemoryStore(ITraceStore traceStore)
    {
        _traceStore = traceStore;
    }

    public global::System.Threading.Tasks.Task SaveAsync(MemoryRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _records[record.Id] = record;
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task SaveEmbeddingAsync(Guid memoryRecordId, IReadOnlyList<float> embedding, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _embeddings[memoryRecordId] = embedding.ToArray();
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<MemoryRecord>> QueryByScopeAsync(string scope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<MemoryRecord> result = _records.Values
            .Where(record => record.Scope == scope)
            .OrderByDescending(record => record.CreatedAtUtc)
            .ToArray();

        return TraceAndReturnAsync("memory-retrieval", scope, result.Count, global::System.Threading.Tasks.Task.FromResult(result), cancellationToken);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<MemoryRecord>> SemanticSearchAsync(
        string scope,
        IReadOnlyList<float> queryEmbedding,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        float[] query = queryEmbedding.ToArray();
        var candidates = _records.Values
            .Where(record => record.Scope == scope && _embeddings.ContainsKey(record.Id))
            .Select(record => new
            {
                Record = record,
                Score = CosineSimilarity(query, _embeddings[record.Id])
            })
            .OrderByDescending(x => x.Score)
            .Take(Math.Max(1, topK))
            .Select(x => x.Record)
            .ToArray();

        return TraceAndReturnAsync("memory-retrieval", scope, candidates.Length, global::System.Threading.Tasks.Task.FromResult<IReadOnlyList<MemoryRecord>>(candidates), cancellationToken);
    }

    private async global::System.Threading.Tasks.Task<IReadOnlyList<MemoryRecord>> TraceAndReturnAsync(
        string category,
        string scope,
        int resultCount,
        global::System.Threading.Tasks.Task<IReadOnlyList<MemoryRecord>> resultTask,
        CancellationToken cancellationToken)
    {
        await _traceStore.RecordAsync(new TraceEntry(
            Id: Guid.NewGuid(),
            Scope: scope,
            Category: category,
            Message: "Memory retrieval executed.",
            Metadata: new Dictionary<string, string>
            {
                ["scope"] = scope,
                ["resultCount"] = resultCount.ToString()
            },
            RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        return await resultTask;
    }

    private static float CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        int length = Math.Min(left.Count, right.Count);
        if (length == 0)
        {
            return 0;
        }

        double dot = 0;
        double normLeft = 0;
        double normRight = 0;

        for (int i = 0; i < length; i++)
        {
            dot += left[i] * right[i];
            normLeft += left[i] * left[i];
            normRight += right[i] * right[i];
        }

        if (normLeft <= 0 || normRight <= 0)
        {
            return 0;
        }

        return (float)(dot / (Math.Sqrt(normLeft) * Math.Sqrt(normRight)));
    }
}

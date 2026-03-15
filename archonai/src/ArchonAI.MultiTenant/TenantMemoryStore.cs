using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using Microsoft.Extensions.Options;

namespace ArchonAI.MultiTenant;

public sealed class TenantMemoryStore : IMemoryStore
{
    private readonly IMemoryStore _inner;
    private readonly IMultiTenantContext _tenantContext;
    private readonly MultiTenantOptions _options;

    public TenantMemoryStore(IMemoryStore inner, IMultiTenantContext tenantContext, IOptions<MultiTenantOptions> options)
    {
        _inner = inner;
        _tenantContext = tenantContext;
        _options = options.Value;
    }

    public async global::System.Threading.Tasks.Task SaveAsync(MemoryRecord record, CancellationToken cancellationToken = default)
    {
        string scoped = Scope(record.Scope);
        IReadOnlyList<MemoryRecord> existing = await _inner.QueryByScopeAsync(scoped, cancellationToken);
        if (existing.Count >= _options.MaxMemoryRecordsPerScopePerTenant)
        {
            throw new InvalidOperationException($"Tenant '{_tenantContext.CurrentTenantId}' exceeded memory scope limit ({_options.MaxMemoryRecordsPerScopePerTenant}).");
        }

        await _inner.SaveAsync(record with { Scope = scoped }, cancellationToken);
    }

    public global::System.Threading.Tasks.Task SaveEmbeddingAsync(Guid memoryRecordId, IReadOnlyList<float> embedding, CancellationToken cancellationToken = default)
        => _inner.SaveEmbeddingAsync(memoryRecordId, embedding, cancellationToken);

    public global::System.Threading.Tasks.Task<IReadOnlyList<MemoryRecord>> QueryByScopeAsync(string scope, CancellationToken cancellationToken = default)
        => _inner.QueryByScopeAsync(Scope(scope), cancellationToken);

    public global::System.Threading.Tasks.Task<IReadOnlyList<MemoryRecord>> SemanticSearchAsync(string scope, IReadOnlyList<float> queryEmbedding, int topK = 5, CancellationToken cancellationToken = default)
        => _inner.SemanticSearchAsync(Scope(scope), queryEmbedding, topK, cancellationToken);

    private string Scope(string rawScope) => $"tenant:{_tenantContext.CurrentTenantId}:{rawScope}";
}

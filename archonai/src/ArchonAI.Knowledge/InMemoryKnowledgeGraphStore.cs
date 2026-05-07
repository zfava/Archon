using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Knowledge;

namespace ArchonAI.Knowledge;

public sealed class InMemoryKnowledgeGraphStore : IKnowledgeGraphStore
{
    private readonly ConcurrentDictionary<string, KnowledgeNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, KnowledgeRelationship> _relationships = new(StringComparer.OrdinalIgnoreCase);

    public global::System.Threading.Tasks.Task UpsertNodeAsync(KnowledgeNode node, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _nodes[node.NodeId] = node with { UpdatedAtUtc = DateTimeOffset.UtcNow };
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task UpsertRelationshipAsync(KnowledgeRelationship relationship, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _relationships[relationship.RelationshipId] = relationship with { UpdatedAtUtc = DateTimeOffset.UtcNow };
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<KnowledgeRelationship>> QueryRelationshipsAsync(string? fromNodeId = null, string? relationshipType = null, string? toNodeId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = _relationships.Values
            .Where(r => string.IsNullOrWhiteSpace(fromNodeId) || r.FromNodeId.Equals(fromNodeId, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrWhiteSpace(relationshipType) || r.RelationshipType.Equals(relationshipType, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrWhiteSpace(toNodeId) || r.ToNodeId.Equals(toNodeId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return global::System.Threading.Tasks.Task.FromResult<IReadOnlyList<KnowledgeRelationship>>(result);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<KnowledgeNode>> QueryRelatedNodesAsync(string nodeId, string? relationshipType = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var relatedIds = _relationships.Values
            .Where(r => r.FromNodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrWhiteSpace(relationshipType) || r.RelationshipType.Equals(relationshipType, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.ToNodeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nodes = _nodes.Values
            .Where(n => relatedIds.Contains(n.NodeId))
            .ToArray();

        return global::System.Threading.Tasks.Task.FromResult<IReadOnlyList<KnowledgeNode>>(nodes);
    }
}

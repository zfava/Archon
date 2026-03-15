using ArchonAI.Core.Models.Knowledge;

namespace ArchonAI.Core.Interfaces;

public interface IKnowledgeGraphStore
{
    global::System.Threading.Tasks.Task UpsertNodeAsync(KnowledgeNode node, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task UpsertRelationshipAsync(KnowledgeRelationship relationship, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<KnowledgeRelationship>> QueryRelationshipsAsync(
        string? fromNodeId = null,
        string? relationshipType = null,
        string? toNodeId = null,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<KnowledgeNode>> QueryRelatedNodesAsync(
        string nodeId,
        string? relationshipType = null,
        CancellationToken cancellationToken = default);
}

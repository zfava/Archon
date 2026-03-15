using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Interfaces.Tooling;
using ArchonAI.Core.Models.Tooling;

namespace ArchonAI.Agents.Tooling;

/// <summary>
/// Tool for graph relationship and neighbor node queries.
/// </summary>
public sealed class KnowledgeGraphQueryTool : IAgentTool
{
    private readonly IKnowledgeGraphStore _knowledgeGraphStore;

    public KnowledgeGraphQueryTool(IKnowledgeGraphStore knowledgeGraphStore)
    {
        _knowledgeGraphStore = knowledgeGraphStore;
    }

    public string Name => "knowledge.query";

    public async global::System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken = default)
    {
        string mode = request.Parameters.GetValueOrDefault("mode", "related");

        if (mode.Equals("relationships", StringComparison.OrdinalIgnoreCase))
        {
            string? fromNodeId = request.Parameters.GetValueOrDefault("fromNodeId");
            string? relationshipType = request.Parameters.GetValueOrDefault("relationshipType");
            string? toNodeId = request.Parameters.GetValueOrDefault("toNodeId");

            var relationships = await _knowledgeGraphStore.QueryRelationshipsAsync(fromNodeId, relationshipType, toNodeId, cancellationToken);
            return new ToolExecutionResult(Name, true, new Dictionary<string, string>
            {
                ["mode"] = "relationships",
                ["count"] = relationships.Count.ToString()
            }, Array.Empty<string>(), DateTimeOffset.UtcNow);
        }

        string nodeId = request.Parameters.GetValueOrDefault("nodeId", string.Empty);
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return new ToolExecutionResult(Name, false, new Dictionary<string, string>(), new[] { "'nodeId' is required for related mode." }, DateTimeOffset.UtcNow);
        }

        string? relType = request.Parameters.GetValueOrDefault("relationshipType");
        var relatedNodes = await _knowledgeGraphStore.QueryRelatedNodesAsync(nodeId, relType, cancellationToken);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["mode"] = "related",
            ["nodeId"] = nodeId,
            ["count"] = relatedNodes.Count.ToString()
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }
}

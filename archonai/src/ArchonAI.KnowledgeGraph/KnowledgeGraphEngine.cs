using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Knowledge;

namespace ArchonAI.KnowledgeGraph;

public sealed class KnowledgeGraphEngine : IKnowledgeGraphEngine
{
    private const string AgentNodeType = "agent";
    private const string TaskNodeType = "task";
    private const string WorkflowNodeType = "workflow";
    private const string OrganizationNodeType = "organization";
    private const string SystemNodeType = "system";
    private const string DataSourceNodeType = "data-source";

    private readonly IKnowledgeGraphStore _store;

    public KnowledgeGraphEngine(IKnowledgeGraphStore store)
    {
        _store = store;
    }

    public async global::System.Threading.Tasks.Task LinkAgentToTaskAsync(Guid agentId, Guid taskId, CancellationToken cancellationToken = default)
    {
        await EnsureNodeAsync($"agent:{agentId}", AgentNodeType, cancellationToken);
        await EnsureNodeAsync($"task:{taskId}", TaskNodeType, cancellationToken);
        await UpsertRelationshipAsync($"agent:{agentId}", "executes", $"task:{taskId}", cancellationToken);
    }

    public async global::System.Threading.Tasks.Task LinkTaskToWorkflowAsync(Guid taskId, Guid workflowId, CancellationToken cancellationToken = default)
    {
        await EnsureNodeAsync($"task:{taskId}", TaskNodeType, cancellationToken);
        await EnsureNodeAsync($"workflow:{workflowId}", WorkflowNodeType, cancellationToken);
        await UpsertRelationshipAsync($"task:{taskId}", "belongs_to", $"workflow:{workflowId}", cancellationToken);
    }

    public async global::System.Threading.Tasks.Task LinkWorkflowToOrganizationAsync(Guid workflowId, string organizationId, CancellationToken cancellationToken = default)
    {
        string normalizedOrganizationId = NormalizeId(organizationId, "default-org");
        await EnsureNodeAsync($"workflow:{workflowId}", WorkflowNodeType, cancellationToken);
        await EnsureNodeAsync($"organization:{normalizedOrganizationId}", OrganizationNodeType, cancellationToken);
        await UpsertRelationshipAsync($"workflow:{workflowId}", "operates_for", $"organization:{normalizedOrganizationId}", cancellationToken);
    }

    public async global::System.Threading.Tasks.Task LinkTaskToSystemAsync(Guid taskId, string systemId, CancellationToken cancellationToken = default)
    {
        string normalizedSystemId = NormalizeId(systemId, "default-system");
        await EnsureNodeAsync($"task:{taskId}", TaskNodeType, cancellationToken);
        await EnsureNodeAsync($"system:{normalizedSystemId}", SystemNodeType, cancellationToken);
        await UpsertRelationshipAsync($"task:{taskId}", "targets", $"system:{normalizedSystemId}", cancellationToken);
    }

    public async global::System.Threading.Tasks.Task LinkSystemToDataSourceAsync(string systemId, string dataSourceId, CancellationToken cancellationToken = default)
    {
        string normalizedSystemId = NormalizeId(systemId, "default-system");
        string normalizedDataSourceId = NormalizeId(dataSourceId, "default-source");
        await EnsureNodeAsync($"system:{normalizedSystemId}", SystemNodeType, cancellationToken);
        await EnsureNodeAsync($"data-source:{normalizedDataSourceId}", DataSourceNodeType, cancellationToken);
        await UpsertRelationshipAsync($"system:{normalizedSystemId}", "fed_by", $"data-source:{normalizedDataSourceId}", cancellationToken);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<KnowledgeNode>> GetSystemsForWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default)
    {
        var taskNodes = await _store.QueryRelatedNodesAsync($"workflow:{workflowId}", "contains", cancellationToken);
        if (taskNodes.Count == 0)
        {
            IReadOnlyList<KnowledgeRelationship> reverseLinks = await _store.QueryRelationshipsAsync(
                toNodeId: $"workflow:{workflowId}",
                relationshipType: "belongs_to",
                cancellationToken: cancellationToken);

            foreach (KnowledgeRelationship relationship in reverseLinks)
            {
                taskNodes = taskNodes.Append(new KnowledgeNode(relationship.FromNodeId, TaskNodeType, relationship.FromNodeId, new Dictionary<string, string>(), DateTimeOffset.UtcNow)).ToArray();
            }
        }

        var systems = new List<KnowledgeNode>();
        foreach (KnowledgeNode taskNode in taskNodes)
        {
            IReadOnlyList<KnowledgeNode> taskSystems = await _store.QueryRelatedNodesAsync(taskNode.NodeId, "targets", cancellationToken);
            systems.AddRange(taskSystems.Where(n => n.NodeType.Equals(SystemNodeType, StringComparison.OrdinalIgnoreCase)));
        }

        return systems
            .GroupBy(node => node.NodeId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<KnowledgeNode>> GetOrganizationsForWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default)
    {
        return _store.QueryRelatedNodesAsync($"workflow:{workflowId}", "operates_for", cancellationToken);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<KnowledgeNode>> GetDataSourcesForWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<KnowledgeNode> systems = await GetSystemsForWorkflowAsync(workflowId, cancellationToken);
        var dataSources = new List<KnowledgeNode>();

        foreach (KnowledgeNode system in systems)
        {
            IReadOnlyList<KnowledgeNode> sources = await _store.QueryRelatedNodesAsync(system.NodeId, "fed_by", cancellationToken);
            dataSources.AddRange(sources.Where(n => n.NodeType.Equals(DataSourceNodeType, StringComparison.OrdinalIgnoreCase)));
        }

        return dataSources
            .GroupBy(node => node.NodeId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<KnowledgeNode>> GetTasksForAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        return _store.QueryRelatedNodesAsync($"agent:{agentId}", "executes", cancellationToken);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<KnowledgeNode>> GetSystemsForTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        return _store.QueryRelatedNodesAsync($"task:{taskId}", "targets", cancellationToken);
    }

    private async global::System.Threading.Tasks.Task EnsureNodeAsync(string nodeId, string nodeType, CancellationToken cancellationToken)
    {
        var node = new KnowledgeNode(
            NodeId: nodeId,
            NodeType: nodeType,
            DisplayName: nodeId,
            Properties: new Dictionary<string, string>(),
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        await _store.UpsertNodeAsync(node, cancellationToken);
    }

    private async global::System.Threading.Tasks.Task UpsertRelationshipAsync(string fromNodeId, string relationshipType, string toNodeId, CancellationToken cancellationToken)
    {
        string relationshipId = $"{fromNodeId}|{relationshipType}|{toNodeId}";
        var relationship = new KnowledgeRelationship(
            RelationshipId: relationshipId,
            FromNodeId: fromNodeId,
            RelationshipType: relationshipType,
            ToNodeId: toNodeId,
            Properties: new Dictionary<string, string>(),
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        await _store.UpsertRelationshipAsync(relationship, cancellationToken);
    }

    private static string NormalizeId(string raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return raw.Trim().Replace(' ', '-').ToLowerInvariant();
    }
}

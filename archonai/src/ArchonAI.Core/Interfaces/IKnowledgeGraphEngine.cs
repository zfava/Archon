using ArchonAI.Core.Models.Knowledge;

namespace ArchonAI.Core.Interfaces;

public interface IKnowledgeGraphEngine
{
    global::System.Threading.Tasks.Task LinkAgentToTaskAsync(Guid agentId, Guid taskId, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task LinkTaskToWorkflowAsync(Guid taskId, Guid workflowId, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task LinkWorkflowToOrganizationAsync(Guid workflowId, string organizationId, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task LinkTaskToSystemAsync(Guid taskId, string systemId, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task LinkSystemToDataSourceAsync(string systemId, string dataSourceId, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<KnowledgeNode>> GetSystemsForWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<KnowledgeNode>> GetOrganizationsForWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<KnowledgeNode>> GetDataSourcesForWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<KnowledgeNode>> GetTasksForAgentAsync(Guid agentId, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<KnowledgeNode>> GetSystemsForTaskAsync(Guid taskId, CancellationToken cancellationToken = default);
}

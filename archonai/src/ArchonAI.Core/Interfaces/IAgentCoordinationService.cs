using ArchonAI.Core.Models.Coordination;

namespace ArchonAI.Core.Interfaces;

public interface IAgentCoordinationService
{
    /// <summary>
    /// An agent requests task support from another agent with the required capability.
    /// The service finds a suitable agent, dispatches the request, and waits for a response
    /// (or times out).
    /// </summary>
    global::System.Threading.Tasks.Task<TaskSupportResponse> RequestTaskSupportAsync(
        TaskSupportRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// An agent shares knowledge (insights, data, context) with a specific agent or broadcasts
    /// to all agents. Knowledge is persisted in the memory store.
    /// </summary>
    global::System.Threading.Tasks.Task ShareKnowledgeAsync(
        KnowledgeSharePayload payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// An agent delegates a task to a specific target agent. The target agent executes the task
    /// and returns the result (or times out).
    /// </summary>
    global::System.Threading.Tasks.Task<TaskDelegationResult> DelegateTaskAsync(
        TaskDelegation delegation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current coordination service status and metrics.
    /// </summary>
    CoordinationStatus GetStatus();
}

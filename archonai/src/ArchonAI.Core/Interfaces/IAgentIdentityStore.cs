using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Identity;

namespace ArchonAI.Core.Interfaces;

public interface IAgentIdentityStore
{
    global::System.Threading.Tasks.Task RegisterOrUpdateAsync(
        Agent agent,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<AgentIdentityProfile?> GetAsync(
        Guid agentId,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task RecordExecutionAsync(
        Guid agentId,
        Guid taskId,
        bool success,
        double executionTimeMs,
        decimal cost,
        string errorType,
        DateTimeOffset executedAtUtc,
        CancellationToken cancellationToken = default);
}

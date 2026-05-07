using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Governance;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Core.Interfaces;

public interface ISecurityPolicyEngine
{
    global::System.Threading.Tasks.Task<SecurityEvaluationResult> EvaluateAgentPermissionsAsync(
        Agent agent,
        CoreTask task,
        CoreExecutionContext context,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<SecurityEvaluationResult> EvaluateDataAccessAsync(
        string subjectId,
        string resourceType,
        string action,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<SecurityEvaluationResult> EvaluateWorkflowLimitsAsync(
        Guid workflowId,
        int stepCount,
        int concurrentAgents,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task AddPolicyAsync(
        SecurityPolicy policy,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task RemovePolicyAsync(
        Guid policyId,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<SecurityPolicy>> GetPoliciesAsync(
        string? category = null,
        CancellationToken cancellationToken = default);

    SecurityMetrics GetMetrics();
}

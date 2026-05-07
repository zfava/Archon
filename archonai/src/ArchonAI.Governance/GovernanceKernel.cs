using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Governance;
using ArchonAI.Core.Models.Identity;
using ArchonAI.Core.Models.Policy;
using Microsoft.Extensions.Options;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Governance;

public sealed class GovernanceKernel : IGovernanceKernel
{
    private readonly GovernanceOptions _options;
    private readonly IPolicyEngine _policyEngine;
    private readonly IAgentIdentityStore _identityStore;
    private readonly ISecurityPolicyEngine _securityPolicyEngine;
    private readonly IAuditLogService _auditLogService;
    private readonly ConcurrentDictionary<Guid, int> _activeExecutionsByAgent = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<DateTimeOffset>> _executionHistoryByAgent = new();

    public GovernanceKernel(
        IOptions<GovernanceOptions> options,
        IPolicyEngine policyEngine,
        IAgentIdentityStore identityStore,
        ISecurityPolicyEngine securityPolicyEngine,
        IAuditLogService auditLogService)
    {
        _options = options.Value;
        _policyEngine = policyEngine;
        _identityStore = identityStore;
        _securityPolicyEngine = securityPolicyEngine;
        _auditLogService = auditLogService;
    }

    public async global::System.Threading.Tasks.Task<GovernanceDecision> ValidateAgentRegistrationAsync(
        Agent agent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var violations = new List<string>();
        if (!_options.AllowAgentRegistration)
        {
            violations.Add("registration-disabled");
        }

        if (string.IsNullOrWhiteSpace(agent.Name))
        {
            violations.Add("agent-name-missing");
        }

        if (agent.Capabilities.Count == 0)
        {
            violations.Add("capability-missing");
        }

        if (_options.AllowedCapabilities.Count > 0)
        {
            var disallowed = agent.Capabilities
                .Select(c => c.Name)
                .Where(c => !_options.AllowedCapabilities.Contains(c, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            if (disallowed.Length > 0)
            {
                violations.Add("disallowed-capabilities");
            }
        }

        AgentIdentityProfile? existingIdentity = await _identityStore.GetAsync(agent.Id, cancellationToken);
        if (existingIdentity is not null)
        {
            if (!existingIdentity.Capabilities.Any(cap =>
                agent.Capabilities.Any(current => current.Name.Equals(cap, StringComparison.OrdinalIgnoreCase))))
            {
                violations.Add("identity-capability-mismatch");
            }
        }

        bool allowed = violations.Count == 0;

        await AuditAsync(
            allowed ? "agent-registration-approved" : "agent-registration-denied",
            "agent",
            agent.Id.ToString(),
            "agent",
            "register",
            "agent",
            agent.Id.ToString(),
            allowed
                ? $"Agent '{agent.Name}' registration approved."
                : $"Agent '{agent.Name}' registration denied: {string.Join(", ", violations)}",
            cancellationToken);

        return new GovernanceDecision(
            IsAllowed: allowed,
            Reason: allowed ? "Agent registration passed governance validation." : $"Agent registration blocked: {string.Join(',', violations)}",
            Violations: violations,
            PolicyDecision: BuildDefaultPolicyDecision(allowed ? "registration-not-applicable" : "registration-blocked"));
    }

    public async global::System.Threading.Tasks.Task<GovernanceDecision> ValidateExecutionAsync(
        Agent agent,
        CoreTask task,
        CoreExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var violations = new List<string>();

        AgentIdentityProfile? identity = await _identityStore.GetAsync(agent.Id, cancellationToken);
        if (identity is null)
        {
            violations.Add("identity-not-found");
        }
        else
        {
            if (!identity.Capabilities.Any(capability => capability.Equals(task.RequiredCapability, StringComparison.OrdinalIgnoreCase)))
            {
                violations.Add("identity-capability-validation-failed");
            }

            if (_options.EnforcePermissionCheck &&
                !identity.Permissions.Any(permission => permission.Equals(_options.RequiredExecutionPermission, StringComparison.OrdinalIgnoreCase)))
            {
                violations.Add("identity-permission-enforcement-failed");
            }

            int totalExecutions = Math.Max(1, identity.PerformanceMetrics.TotalExecutions);
            double successRate = (double)identity.PerformanceMetrics.SuccessfulExecutions / totalExecutions;
            if (identity.PerformanceMetrics.TotalExecutions >= Math.Max(1, _options.MinExecutionsForPerformanceGate)
                && successRate < _options.MinIdentitySuccessRate)
            {
                violations.Add("identity-performance-below-threshold");
            }
        }

        if (!agent.Capabilities.Any(capability => capability.Name.Equals(task.RequiredCapability, StringComparison.OrdinalIgnoreCase)))
        {
            violations.Add("capability-validation-failed");
        }

        if (_options.EnforcePermissionCheck)
        {
            bool hasPermission = context.Metadata.TryGetValue("permissions", out string? permissions)
                && permissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(permission => permission.Equals(_options.RequiredExecutionPermission, StringComparison.OrdinalIgnoreCase));

            if (!hasPermission)
            {
                violations.Add("permission-enforcement-failed");
            }
        }

        int activeExecutions = _activeExecutionsByAgent.GetOrAdd(agent.Id, 0);
        if (activeExecutions >= Math.Max(1, _options.MaxConcurrentExecutionsPerAgent))
        {
            violations.Add("resource-quota-concurrent-exceeded");
        }

        var history = _executionHistoryByAgent.GetOrAdd(agent.Id, _ => new ConcurrentQueue<DateTimeOffset>());
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddHours(-1);
        while (history.TryPeek(out DateTimeOffset timestamp) && timestamp < cutoff)
        {
            history.TryDequeue(out _);
        }

        if (history.Count >= Math.Max(1, _options.MaxExecutionsPerHourPerAgent))
        {
            violations.Add("resource-quota-hourly-exceeded");
        }

        // Security policy enforcement: agent permissions
        SecurityEvaluationResult securityResult = await _securityPolicyEngine.EvaluateAgentPermissionsAsync(
            agent, task, context, cancellationToken);
        if (!securityResult.IsAllowed)
        {
            foreach (string violation in securityResult.Violations)
            {
                violations.Add($"security:{violation}");
            }
        }

        PolicyDecision policyDecision = await _policyEngine.EvaluateAsync(agent, task, context, cancellationToken);
        if (!policyDecision.IsAllowed)
        {
            violations.Add("policy-check-failed");
        }

        bool isAllowed = violations.Count == 0 && policyDecision.IsAllowed;
        if (isAllowed)
        {
            _activeExecutionsByAgent.AddOrUpdate(agent.Id, 1, (_, current) => current + 1);
            history.Enqueue(DateTimeOffset.UtcNow);
        }

        await AuditAsync(
            isAllowed ? "execution-approved" : "execution-denied",
            "agent",
            agent.Id.ToString(),
            "agent",
            "execute",
            "task",
            task.Id.ToString(),
            isAllowed
                ? $"Execution approved for agent '{agent.Name}' on task '{task.Name}'."
                : $"Execution denied for agent '{agent.Name}': {string.Join(", ", violations)}",
            cancellationToken);

        return new GovernanceDecision(
            IsAllowed: isAllowed,
            Reason: isAllowed ? "Governance checks passed." : $"Governance blocked execution: {string.Join(',', violations)}",
            Violations: violations,
            PolicyDecision: policyDecision);
    }

    public async global::System.Threading.Tasks.Task MarkExecutionCompletedAsync(
        Guid agentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _activeExecutionsByAgent.AddOrUpdate(agentId, 0, (_, current) => Math.Max(0, current - 1));

        await AuditAsync(
            "execution-completed",
            "agent",
            agentId.ToString(),
            "agent",
            "complete",
            "execution",
            agentId.ToString(),
            $"Execution slot released for agent {agentId}.",
            cancellationToken);
    }

    private async global::System.Threading.Tasks.Task AuditAsync(
        string eventType,
        string category,
        string subjectId,
        string subjectType,
        string action,
        string resourceType,
        string resourceId,
        string description,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditLogService.RecordAsync(
                eventType, category, nameof(GovernanceKernel),
                subjectId, subjectType, action,
                resourceType, resourceId, description,
                ct: cancellationToken);
        }
        catch
        {
            // Audit failures must not break governance
        }
    }

    private static PolicyDecision BuildDefaultPolicyDecision(string reason)
    {
        return new PolicyDecision(
            IsAllowed: true,
            RiskScore: 0,
            ConfidenceScore: 1,
            RequiresApproval: false,
            ApprovalState: "not-applicable",
            ManualOverrideState: "none",
            ApprovalCheckpoint: "none",
            GuardrailViolations: Array.Empty<string>(),
            Reason: reason,
            EvaluatedAtUtc: DateTimeOffset.UtcNow);
    }
}

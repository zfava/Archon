using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Governance;
using ArchonAI.Core.Models.Policy;
using Microsoft.Extensions.Options;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Governance;

public sealed class GovernanceKernel : IGovernanceKernel
{
    private readonly GovernanceOptions _options;
    private readonly IPolicyEngine _policyEngine;
    private readonly ConcurrentDictionary<Guid, int> _activeExecutionsByAgent = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<DateTimeOffset>> _executionHistoryByAgent = new();

    public GovernanceKernel(IOptions<GovernanceOptions> options, IPolicyEngine policyEngine)
    {
        _options = options.Value;
        _policyEngine = policyEngine;
    }

    public global::System.Threading.Tasks.Task<GovernanceDecision> ValidateAgentRegistrationAsync(
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

        bool allowed = violations.Count == 0;
        return global::System.Threading.Tasks.Task.FromResult(new GovernanceDecision(
            IsAllowed: allowed,
            Reason: allowed ? "Agent registration passed governance validation." : $"Agent registration blocked: {string.Join(',', violations)}",
            Violations: violations,
            PolicyDecision: BuildDefaultPolicyDecision(allowed ? "registration-not-applicable" : "registration-blocked")));
    }

    public async global::System.Threading.Tasks.Task<GovernanceDecision> ValidateExecutionAsync(
        Agent agent,
        CoreTask task,
        CoreExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var violations = new List<string>();

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

        return new GovernanceDecision(
            IsAllowed: isAllowed,
            Reason: isAllowed ? "Governance checks passed." : $"Governance blocked execution: {string.Join(',', violations)}",
            Violations: violations,
            PolicyDecision: policyDecision);
    }

    public global::System.Threading.Tasks.Task MarkExecutionCompletedAsync(
        Guid agentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _activeExecutionsByAgent.AddOrUpdate(agentId, 0, (_, current) => Math.Max(0, current - 1));
        return global::System.Threading.Tasks.Task.CompletedTask;
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

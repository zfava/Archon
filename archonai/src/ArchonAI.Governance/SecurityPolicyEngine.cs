using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Governance;
using Microsoft.Extensions.Options;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Governance;

public sealed class SecurityPolicyEngine : ISecurityPolicyEngine
{
    private readonly ConcurrentDictionary<Guid, SecurityPolicy> _policies = new();
    private readonly ConcurrentDictionary<string, ViolationCounter> _violationCounters = new(StringComparer.OrdinalIgnoreCase);
    private readonly GovernanceOptions _options;
    private readonly IAuditLogService _auditLogService;

    private long _totalEvaluations;
    private long _totalViolations;
    private long _agentPermissionDenials;
    private long _dataAccessDenials;
    private long _workflowLimitBreaches;

    public SecurityPolicyEngine(IOptions<GovernanceOptions> options, IAuditLogService auditLogService)
    {
        _options = options.Value;
        _auditLogService = auditLogService;
        InitializeDefaultPolicies();
    }

    public async global::System.Threading.Tasks.Task<SecurityEvaluationResult> EvaluateAgentPermissionsAsync(
        Agent agent,
        CoreTask task,
        CoreExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _totalEvaluations);

        var evaluations = new List<SecurityPolicyEvaluation>();
        var violations = new List<string>();

        foreach (var policy in GetEnabledPolicies("agent-permissions"))
        {
            bool passed = EvaluateAgentPolicy(policy, agent, task, context);
            evaluations.Add(new SecurityPolicyEvaluation(policy.Id, policy.Name, passed,
                passed ? "Agent satisfies permission policy." : $"Agent violates policy: {policy.Name}"));

            if (!passed)
            {
                violations.Add($"agent-permission:{policy.Name}");
                RecordViolation(policy.Name, "agent-permissions");
            }
        }

        bool isAllowed = violations.Count == 0;
        if (!isAllowed)
        {
            Interlocked.Increment(ref _agentPermissionDenials);
            await AuditViolationAsync("agent-permission-denied", "agent-permissions",
                agent.Id.ToString(), "agent", task.RequiredCapability, violations, cancellationToken);
        }

        return new SecurityEvaluationResult(isAllowed, evaluations, violations, DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task<SecurityEvaluationResult> EvaluateDataAccessAsync(
        string subjectId,
        string resourceType,
        string action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _totalEvaluations);

        var evaluations = new List<SecurityPolicyEvaluation>();
        var violations = new List<string>();

        foreach (var policy in GetEnabledPolicies("data-access"))
        {
            bool passed = EvaluateDataPolicy(policy, resourceType, action);
            evaluations.Add(new SecurityPolicyEvaluation(policy.Id, policy.Name, passed,
                passed ? "Data access permitted." : $"Data access blocked by policy: {policy.Name}"));

            if (!passed)
            {
                violations.Add($"data-access:{policy.Name}:{resourceType}:{action}");
                RecordViolation(policy.Name, "data-access");
            }
        }

        bool isAllowed = violations.Count == 0;
        if (!isAllowed)
        {
            Interlocked.Increment(ref _dataAccessDenials);
            await AuditViolationAsync("data-access-denied", "data-access",
                subjectId, "subject", $"{resourceType}:{action}", violations, cancellationToken);
        }

        return new SecurityEvaluationResult(isAllowed, evaluations, violations, DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task<SecurityEvaluationResult> EvaluateWorkflowLimitsAsync(
        Guid workflowId,
        int stepCount,
        int concurrentAgents,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _totalEvaluations);

        var evaluations = new List<SecurityPolicyEvaluation>();
        var violations = new List<string>();

        foreach (var policy in GetEnabledPolicies("workflow-limits"))
        {
            bool passed = EvaluateWorkflowPolicy(policy, stepCount, concurrentAgents);
            evaluations.Add(new SecurityPolicyEvaluation(policy.Id, policy.Name, passed,
                passed ? "Workflow within limits." : $"Workflow exceeds limit: {policy.Name}"));

            if (!passed)
            {
                violations.Add($"workflow-limit:{policy.Name}");
                RecordViolation(policy.Name, "workflow-limits");
            }
        }

        bool isAllowed = violations.Count == 0;
        if (!isAllowed)
        {
            Interlocked.Increment(ref _workflowLimitBreaches);
            await AuditViolationAsync("workflow-limit-breach", "workflow-limits",
                workflowId.ToString(), "workflow", $"steps={stepCount},agents={concurrentAgents}", violations, cancellationToken);
        }

        return new SecurityEvaluationResult(isAllowed, evaluations, violations, DateTimeOffset.UtcNow);
    }

    public global::System.Threading.Tasks.Task AddPolicyAsync(SecurityPolicy policy, CancellationToken cancellationToken = default)
    {
        _policies[policy.Id] = policy;
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task RemovePolicyAsync(Guid policyId, CancellationToken cancellationToken = default)
    {
        _policies.TryRemove(policyId, out _);
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<SecurityPolicy>> GetPoliciesAsync(
        string? category = null, CancellationToken cancellationToken = default)
    {
        IEnumerable<SecurityPolicy> query = _policies.Values;
        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(p => p.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        IReadOnlyList<SecurityPolicy> result = query.OrderBy(p => p.Category).ThenBy(p => p.Name).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public SecurityMetrics GetMetrics()
    {
        var topViolations = _violationCounters.Values
            .OrderByDescending(v => v.Count)
            .Take(10)
            .Select(v => new SecurityPolicyViolationSummary(v.PolicyName, v.Category, v.Count, v.LastOccurred))
            .ToList();

        return new SecurityMetrics(
            TotalEvaluations: Interlocked.Read(ref _totalEvaluations),
            TotalViolations: Interlocked.Read(ref _totalViolations),
            AgentPermissionDenials: Interlocked.Read(ref _agentPermissionDenials),
            DataAccessDenials: Interlocked.Read(ref _dataAccessDenials),
            WorkflowLimitBreaches: Interlocked.Read(ref _workflowLimitBreaches),
            ActivePolicies: _policies.Values.Count(p => p.IsEnabled),
            TopViolations: topViolations,
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    private IEnumerable<SecurityPolicy> GetEnabledPolicies(string category)
    {
        return _policies.Values
            .Where(p => p.IsEnabled && p.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
    }

    private bool EvaluateAgentPolicy(SecurityPolicy policy, Agent agent, CoreTask task, CoreExecutionContext context)
    {
        var rule = policy.Rule;

        if (rule.RuleType == "capability-allowlist" && rule.AllowedValues.Count > 0)
        {
            return agent.Capabilities.Any(c =>
                rule.AllowedValues.Contains(c.Name, StringComparer.OrdinalIgnoreCase));
        }

        if (rule.RuleType == "capability-denylist" && rule.DeniedValues.Count > 0)
        {
            return !agent.Capabilities.Any(c =>
                rule.DeniedValues.Contains(c.Name, StringComparer.OrdinalIgnoreCase));
        }

        if (rule.RuleType == "permission-required")
        {
            if (!context.Metadata.TryGetValue("permissions", out string? perms))
                return false;

            var permissionSet = perms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return rule.AllowedValues.All(required =>
                permissionSet.Contains(required, StringComparer.OrdinalIgnoreCase));
        }

        return true;
    }

    private static bool EvaluateDataPolicy(SecurityPolicy policy, string resourceType, string action)
    {
        var rule = policy.Rule;

        if (rule.RuleType == "resource-denylist" && rule.DeniedValues.Count > 0)
        {
            return !rule.DeniedValues.Contains(resourceType, StringComparer.OrdinalIgnoreCase);
        }

        if (rule.RuleType == "action-allowlist" && rule.AllowedValues.Count > 0)
        {
            return rule.AllowedValues.Contains(action, StringComparer.OrdinalIgnoreCase);
        }

        if (rule.RuleType == "resource-action-matrix")
        {
            string key = $"{resourceType}:{action}";
            if (rule.Limits.TryGetValue(key, out string? allowed))
            {
                return allowed.Equals("allow", StringComparison.OrdinalIgnoreCase);
            }
            // Not in matrix defaults to allow
        }

        return true;
    }

    private static bool EvaluateWorkflowPolicy(SecurityPolicy policy, int stepCount, int concurrentAgents)
    {
        var rule = policy.Rule;

        if (rule.RuleType == "workflow-limits")
        {
            if (rule.Limits.TryGetValue("maxSteps", out string? maxStepsStr) &&
                int.TryParse(maxStepsStr, out int maxSteps) && stepCount > maxSteps)
            {
                return false;
            }

            if (rule.Limits.TryGetValue("maxConcurrentAgents", out string? maxAgentsStr) &&
                int.TryParse(maxAgentsStr, out int maxAgents) && concurrentAgents > maxAgents)
            {
                return false;
            }
        }

        return true;
    }

    private void RecordViolation(string policyName, string category)
    {
        Interlocked.Increment(ref _totalViolations);

        string key = $"{category}::{policyName}";
        _violationCounters.AddOrUpdate(
            key,
            _ => new ViolationCounter(policyName, category),
            (_, existing) =>
            {
                existing.Increment();
                return existing;
            });
    }

    private async global::System.Threading.Tasks.Task AuditViolationAsync(
        string eventType,
        string category,
        string subjectId,
        string subjectType,
        string action,
        IReadOnlyList<string> violations,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditLogService.RecordAsync(
                eventType: eventType,
                category: "security",
                source: nameof(SecurityPolicyEngine),
                subjectId: subjectId,
                subjectType: subjectType,
                action: action,
                resourceType: category,
                resourceId: string.Empty,
                description: $"Security policy violation: {string.Join(", ", violations)}",
                metadata: new Dictionary<string, string>
                {
                    ["violationCount"] = violations.Count.ToString(),
                    ["violations"] = string.Join("|", violations)
                },
                ct: cancellationToken);
        }
        catch
        {
            // Audit failures must not break policy enforcement
        }
    }

    private void InitializeDefaultPolicies()
    {
        var now = DateTimeOffset.UtcNow;

        AddDefaultPolicy("deny-dangerous-capabilities", "agent-permissions",
            new SecurityPolicyRule("capability-denylist",
                AllowedValues: Array.Empty<string>(),
                DeniedValues: new[] { "system-admin", "unrestricted-network", "raw-database-access" },
                Limits: new Dictionary<string, string>()),
            now);

        AddDefaultPolicy("require-execute-permission", "agent-permissions",
            new SecurityPolicyRule("permission-required",
                AllowedValues: new[] { _options.RequiredExecutionPermission },
                DeniedValues: Array.Empty<string>(),
                Limits: new Dictionary<string, string>()),
            now);

        AddDefaultPolicy("deny-sensitive-data", "data-access",
            new SecurityPolicyRule("resource-denylist",
                AllowedValues: Array.Empty<string>(),
                DeniedValues: new[] { "credentials", "encryption-keys", "pii-unmasked" },
                Limits: new Dictionary<string, string>()),
            now);

        AddDefaultPolicy("read-only-data-default", "data-access",
            new SecurityPolicyRule("action-allowlist",
                AllowedValues: new[] { "read", "query", "list", "search" },
                DeniedValues: Array.Empty<string>(),
                Limits: new Dictionary<string, string>()),
            now);

        AddDefaultPolicy("workflow-resource-limits", "workflow-limits",
            new SecurityPolicyRule("workflow-limits",
                AllowedValues: Array.Empty<string>(),
                DeniedValues: Array.Empty<string>(),
                Limits: new Dictionary<string, string>
                {
                    ["maxSteps"] = "50",
                    ["maxConcurrentAgents"] = "20"
                }),
            now);
    }

    private void AddDefaultPolicy(string name, string category, SecurityPolicyRule rule, DateTimeOffset now)
    {
        var policy = new SecurityPolicy(
            Id: Guid.NewGuid(),
            Name: name,
            Category: category,
            Rule: rule,
            IsEnabled: true,
            CreatedAtUtc: now,
            UpdatedAtUtc: now);

        _policies[policy.Id] = policy;
    }

    private sealed class ViolationCounter
    {
        public string PolicyName { get; }
        public string Category { get; }
        public long Count => Interlocked.Read(ref _count);
        public DateTimeOffset LastOccurred { get; private set; }
        private long _count = 1;

        public ViolationCounter(string policyName, string category)
        {
            PolicyName = policyName;
            Category = category;
            LastOccurred = DateTimeOffset.UtcNow;
        }

        public void Increment()
        {
            Interlocked.Increment(ref _count);
            LastOccurred = DateTimeOffset.UtcNow;
        }
    }
}

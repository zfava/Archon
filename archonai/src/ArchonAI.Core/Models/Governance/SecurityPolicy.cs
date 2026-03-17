namespace ArchonAI.Core.Models.Governance;

public sealed record SecurityPolicy(
    Guid Id,
    string Name,
    string Category,
    SecurityPolicyRule Rule,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record SecurityPolicyRule(
    string RuleType,
    IReadOnlyList<string> AllowedValues,
    IReadOnlyList<string> DeniedValues,
    IReadOnlyDictionary<string, string> Limits);

public sealed record SecurityPolicyEvaluation(
    Guid PolicyId,
    string PolicyName,
    bool Passed,
    string Reason);

public sealed record SecurityEvaluationResult(
    bool IsAllowed,
    IReadOnlyList<SecurityPolicyEvaluation> Evaluations,
    IReadOnlyList<string> Violations,
    DateTimeOffset EvaluatedAtUtc);

public sealed record SecurityMetrics(
    long TotalEvaluations,
    long TotalViolations,
    long AgentPermissionDenials,
    long DataAccessDenials,
    long WorkflowLimitBreaches,
    int ActivePolicies,
    IReadOnlyList<SecurityPolicyViolationSummary> TopViolations,
    DateTimeOffset GeneratedAtUtc);

public sealed record SecurityPolicyViolationSummary(
    string PolicyName,
    string Category,
    long ViolationCount,
    DateTimeOffset LastOccurredAtUtc);

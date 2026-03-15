namespace ArchonAI.Core.Models.Policy;

public sealed record PolicyDecision(
    bool IsAllowed,
    double RiskScore,
    double ConfidenceScore,
    bool RequiresApproval,
    string ApprovalState,
    string ManualOverrideState,
    string ApprovalCheckpoint,
    IReadOnlyList<string> GuardrailViolations,
    string Reason,
    DateTimeOffset EvaluatedAtUtc);

namespace ArchonAI.Api.Dtos;

public sealed record ExternalEvaluationRequest(
    string TaskDescription,
    string RequiredCapability,
    string ActionScope,
    Dictionary<string, string>? ContextMetadata,
    string CallerIdentity,
    string OrganizationId);

public sealed record ExternalEvaluationResponse(
    string Decision,
    double Confidence,
    string TrustTierApplied,
    double RiskScore,
    IReadOnlyList<string> Violations,
    IReadOnlyList<string> GuardrailFindings,
    Guid? ApprovalGateId,
    Guid EvaluationId,
    DateTimeOffset EvaluatedAt,
    string SignedDigest);

public sealed record ExternalApprovalStatusRequest(
    string CallerIdentity);

public sealed record ExternalApprovalStatusResponse(
    Guid GateId,
    string Status,
    string? ReviewedBy,
    string? ReviewNotes,
    DateTimeOffset? ReviewedAtUtc);

public sealed record ExternalTrustTierResponse(
    string ActionScope,
    string EffectiveTier,
    int TierLevel);

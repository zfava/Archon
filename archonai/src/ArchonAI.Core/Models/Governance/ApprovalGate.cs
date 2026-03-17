namespace ArchonAI.Core.Models.Governance;

/// <summary>
/// Represents an approval requirement for a high-risk action.
/// </summary>
public sealed record ApprovalGate(
    Guid Id,
    string ActionType,
    string ResourceId,
    string TenantId,
    string RequestedBy,
    string Justification,
    ApprovalStatus Status,
    string? ReviewedBy,
    string? ReviewNotes,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? ReviewedAtUtc);

public enum ApprovalStatus
{
    Pending,
    Approved,
    Denied,
    Expired
}

/// <summary>
/// Defines which action types require approval and who can approve them.
/// </summary>
public sealed record ApprovalPolicy(
    Guid Id,
    string ActionType,
    string Description,
    string RequiredApproverRole,
    bool RequireSeparationOfDuties,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Audit record linking an approval decision to the action it authorized.
/// </summary>
public sealed record ApprovalAuditEntry(
    Guid Id,
    Guid ApprovalGateId,
    string ActionType,
    string TenantId,
    string RequestedBy,
    string? ReviewedBy,
    ApprovalStatus Outcome,
    DateTimeOffset OccurredAtUtc);

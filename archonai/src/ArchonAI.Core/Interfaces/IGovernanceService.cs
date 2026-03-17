using ArchonAI.Core.Models.Governance;

namespace ArchonAI.Core.Interfaces;

public interface IGovernanceService
{
    // Approval gates
    Task<ApprovalGate> RequestApprovalAsync(
        string actionType, string resourceId, string tenantId,
        string requestedBy, string justification,
        string? actionPayload = null, CancellationToken ct = default);
    Task<ApprovalGate?> GetApprovalAsync(Guid gateId, string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<ApprovalGate>> ListPendingApprovalsAsync(string tenantId, CancellationToken ct = default);
    Task<ApprovalGate> ReviewApprovalAsync(
        Guid gateId, string tenantId, string reviewedBy, string reviewerRole,
        bool approve, string? notes, CancellationToken ct = default);
    Task<ApprovalGate> RecordExecutionResultAsync(
        Guid gateId, GateExecutionStatus status, string? error, CancellationToken ct = default);

    // Approval policies
    Task<IReadOnlyList<ApprovalPolicy>> ListApprovalPoliciesAsync(CancellationToken ct = default);
    Task<ApprovalPolicy> CreateApprovalPolicyAsync(
        string actionType, string description, string requiredApproverRole,
        bool requireSeparationOfDuties, CancellationToken ct = default);
    Task<bool> RequiresApprovalAsync(string actionType, CancellationToken ct = default);

    // Audit trail
    Task<IReadOnlyList<ApprovalAuditEntry>> GetApprovalHistoryAsync(
        string? tenantId, string? actionType, int limit, CancellationToken ct = default);
}

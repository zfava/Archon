using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Governance;

namespace ArchonAI.Api.Security;

public sealed class GovernanceService : IGovernanceService
{
    private readonly ConcurrentDictionary<Guid, ApprovalGate> _gates = new();
    private readonly ConcurrentDictionary<Guid, ApprovalPolicy> _policies = new();
    private readonly ConcurrentBag<ApprovalAuditEntry> _auditEntries = new();

    public GovernanceService()
    {
        SeedDefaultPolicies();
    }

    private void SeedDefaultPolicies()
    {
        var now = DateTimeOffset.UtcNow;
        var policies = new[]
        {
            new ApprovalPolicy(Guid.NewGuid(), "workflow.cancel", "Cancelling a running workflow",
                "Admin", true, true, now),
            new ApprovalPolicy(Guid.NewGuid(), "policy.delete", "Deleting a governance policy",
                "Admin", true, true, now),
            new ApprovalPolicy(Guid.NewGuid(), "rbac.role.delete", "Deleting an RBAC role",
                "Admin", false, true, now),
            new ApprovalPolicy(Guid.NewGuid(), "connector.disconnect", "Disconnecting an active integration",
                "Admin", true, true, now),
            new ApprovalPolicy(Guid.NewGuid(), "strategy.override", "Overriding an AI-selected strategy",
                "Admin", true, true, now),
        };

        foreach (var p in policies)
            _policies[p.Id] = p;
    }

    public Task<ApprovalGate> RequestApprovalAsync(
        string actionType, string resourceId, string tenantId,
        string requestedBy, string justification,
        string? actionPayload = null, CancellationToken ct = default)
    {
        // Deduplication: return existing pending gate for same (actionType, resourceId, tenant)
        var existing = _gates.Values.FirstOrDefault(g =>
            g.Status == ApprovalStatus.Pending
            && g.ActionType == actionType
            && g.ResourceId == resourceId
            && string.Equals(g.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
            return Task.FromResult(existing);

        var gate = new ApprovalGate(
            Id: Guid.NewGuid(),
            ActionType: actionType,
            ResourceId: resourceId,
            TenantId: tenantId,
            RequestedBy: requestedBy,
            Justification: justification,
            Status: ApprovalStatus.Pending,
            ReviewedBy: null,
            ReviewNotes: null,
            RequestedAtUtc: DateTimeOffset.UtcNow,
            ReviewedAtUtc: null)
        {
            ActionPayload = actionPayload
        };

        _gates[gate.Id] = gate;
        return Task.FromResult(gate);
    }

    public Task<ApprovalGate?> GetApprovalAsync(Guid gateId, string tenantId, CancellationToken ct = default)
    {
        _gates.TryGetValue(gateId, out var gate);
        // Enforce tenant isolation: only return if gate belongs to caller's tenant
        if (gate is not null && !string.Equals(gate.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<ApprovalGate?>(null);
        return Task.FromResult(gate);
    }

    public Task<IReadOnlyList<ApprovalGate>> ListPendingApprovalsAsync(
        string tenantId, CancellationToken ct = default)
    {
        IReadOnlyList<ApprovalGate> result = _gates.Values
            .Where(g => g.TenantId == tenantId && g.Status == ApprovalStatus.Pending)
            .OrderByDescending(g => g.RequestedAtUtc)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<ApprovalGate> ReviewApprovalAsync(
        Guid gateId, string tenantId, string reviewedBy, string reviewerRole,
        bool approve, string? notes, CancellationToken ct = default)
    {
        if (!_gates.TryGetValue(gateId, out var gate))
            throw new KeyNotFoundException($"Approval gate {gateId} not found.");

        // Enforce tenant isolation: reviewer must be in the same tenant as the gate
        if (!string.Equals(gate.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            throw new KeyNotFoundException($"Approval gate {gateId} not found.");

        if (gate.Status != ApprovalStatus.Pending)
            throw new InvalidOperationException($"Approval gate {gateId} is already {gate.Status}.");

        var policy = _policies.Values.FirstOrDefault(p =>
            p.ActionType == gate.ActionType && p.IsEnabled);

        // Enforce RequiredApproverRole: reviewer must hold the required role
        if (policy is not null
            && !string.Equals(reviewerRole, policy.RequiredApproverRole, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Reviewer role '{reviewerRole}' does not satisfy required approver role '{policy.RequiredApproverRole}'.");
        }

        // Separation of duties: reviewer cannot be the requester
        if (policy?.RequireSeparationOfDuties == true
            && string.Equals(gate.RequestedBy, reviewedBy, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Separation of duties violation: the requester cannot approve their own request.");
        }

        var status = approve ? ApprovalStatus.Approved : ApprovalStatus.Denied;
        var reviewed = gate with
        {
            Status = status,
            ReviewedBy = reviewedBy,
            ReviewNotes = notes,
            ReviewedAtUtc = DateTimeOffset.UtcNow,
        };
        _gates[gateId] = reviewed;

        // Record audit entry
        _auditEntries.Add(new ApprovalAuditEntry(
            Id: Guid.NewGuid(),
            ApprovalGateId: gateId,
            ActionType: gate.ActionType,
            TenantId: gate.TenantId,
            RequestedBy: gate.RequestedBy,
            ReviewedBy: reviewedBy,
            Outcome: status,
            OccurredAtUtc: DateTimeOffset.UtcNow));

        return Task.FromResult(reviewed);
    }

    public Task<IReadOnlyList<ApprovalPolicy>> ListApprovalPoliciesAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ApprovalPolicy> result = _policies.Values.ToList();
        return Task.FromResult(result);
    }

    public Task<ApprovalPolicy> CreateApprovalPolicyAsync(
        string actionType, string description, string requiredApproverRole,
        bool requireSeparationOfDuties, CancellationToken ct = default)
    {
        var policy = new ApprovalPolicy(
            Id: Guid.NewGuid(),
            ActionType: actionType,
            Description: description,
            RequiredApproverRole: requiredApproverRole,
            RequireSeparationOfDuties: requireSeparationOfDuties,
            IsEnabled: true,
            CreatedAtUtc: DateTimeOffset.UtcNow);

        _policies[policy.Id] = policy;
        return Task.FromResult(policy);
    }

    public Task<bool> RequiresApprovalAsync(string actionType, CancellationToken ct = default)
    {
        var requires = _policies.Values.Any(p =>
            p.ActionType == actionType && p.IsEnabled);
        return Task.FromResult(requires);
    }

    public Task<IReadOnlyList<ApprovalAuditEntry>> GetApprovalHistoryAsync(
        string? tenantId, string? actionType, int limit, CancellationToken ct = default)
    {
        var query = _auditEntries.AsEnumerable();
        if (tenantId is not null) query = query.Where(e => e.TenantId == tenantId);
        if (actionType is not null) query = query.Where(e => e.ActionType == actionType);

        IReadOnlyList<ApprovalAuditEntry> result = query
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(limit)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<ApprovalGate> RecordExecutionResultAsync(
        Guid gateId, GateExecutionStatus status, string? error, CancellationToken ct = default)
    {
        if (!_gates.TryGetValue(gateId, out var gate))
            throw new KeyNotFoundException($"Approval gate {gateId} not found.");

        var updated = gate with
        {
            ExecutionStatus = status,
            ExecutionError = error,
            ExecutedAtUtc = DateTimeOffset.UtcNow,
        };
        _gates[gateId] = updated;
        return Task.FromResult(updated);
    }
}

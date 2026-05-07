using System.Collections.Concurrent;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Rbac;
using OTel = ArchonAI.Common.Observability.Telemetry;

namespace ArchonAI.Api.Security;

public sealed class RbacService : IRbacService
{
    private readonly ConcurrentDictionary<Guid, RbacRole> _roles = new();
    private readonly ConcurrentDictionary<Guid, RoleAssignment> _assignments = new();
    private readonly ConcurrentDictionary<Guid, PermissionPolicy> _policies = new();
    private readonly IEventBus _eventBus;

    private long _accessChecks;
    private long _accessDenials;

    private static readonly IReadOnlyList<string> AllPermissions =
    [
        "agents:read", "agents:write", "agents:execute",
        "workflows:read", "workflows:write", "workflows:execute",
        "connectors:read", "connectors:write", "connectors:execute",
        "admin:read", "admin:write",
        "policy:read", "policy:write",
        "monitoring:read",
        "rbac:read", "rbac:write"
    ];

    private static readonly IReadOnlyList<string> OperatorPermissions =
    [
        "agents:read", "agents:execute",
        "workflows:read", "workflows:execute",
        "connectors:read", "connectors:execute",
        "monitoring:read",
        "policy:read",
        "rbac:read"
    ];

    private static readonly IReadOnlyList<string> ViewerPermissions =
    [
        "agents:read",
        "workflows:read",
        "connectors:read",
        "monitoring:read",
        "policy:read",
        "rbac:read"
    ];

    public RbacService(IEventBus eventBus)
    {
        _eventBus = eventBus;
        SeedDefaultRoles();
    }

    private void SeedDefaultRoles()
    {
        var now = DateTimeOffset.UtcNow;

        var adminRole = new RbacRole(Guid.NewGuid(), "Admin", "Full administrative access to all resources", AllPermissions, true, now);
        var operatorRole = new RbacRole(Guid.NewGuid(), "Operator", "Read and execute access to operational resources", OperatorPermissions, true, now);
        var viewerRole = new RbacRole(Guid.NewGuid(), "Viewer", "Read-only access to all resources", ViewerPermissions, true, now);

        _roles[adminRole.Id] = adminRole;
        _roles[operatorRole.Id] = operatorRole;
        _roles[viewerRole.Id] = viewerRole;
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<RbacRole>> GetRolesAsync(CancellationToken ct = default)
    {
        IReadOnlyList<RbacRole> result = _roles.Values.ToList().AsReadOnly();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public global::System.Threading.Tasks.Task<RbacRole?> GetRoleAsync(Guid roleId, CancellationToken ct = default)
    {
        _roles.TryGetValue(roleId, out var role);
        return global::System.Threading.Tasks.Task.FromResult(role);
    }

    public async global::System.Threading.Tasks.Task<RbacRole> CreateRoleAsync(string name, string description, IReadOnlyList<string> permissions, CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("RbacService.CreateRole");
        activity?.SetTag("rbac.role.name", name);

        var role = new RbacRole(Guid.NewGuid(), name, description, permissions, false, DateTimeOffset.UtcNow);
        _roles[role.Id] = role;

        OTel.RbacRoleChanges.Add(1);

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "rbac.role.created",
            "RbacService",
            Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["roleId"] = role.Id.ToString(),
                ["roleName"] = name,
                ["permissionCount"] = permissions.Count.ToString()
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);

        return role;
    }

    public async global::System.Threading.Tasks.Task UpdateRoleAsync(Guid roleId, string description, IReadOnlyList<string> permissions, CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("RbacService.UpdateRole");
        activity?.SetTag("rbac.role.id", roleId.ToString());

        if (!_roles.TryGetValue(roleId, out var existing))
            throw new KeyNotFoundException($"Role {roleId} not found.");

        if (existing.IsSystem)
            throw new InvalidOperationException("System roles cannot be modified.");

        var updated = existing with { Description = description, Permissions = permissions };
        _roles[roleId] = updated;

        OTel.RbacRoleChanges.Add(1);

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "rbac.role.updated",
            "RbacService",
            Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["roleId"] = roleId.ToString(),
                ["roleName"] = updated.Name
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);
    }

    public async global::System.Threading.Tasks.Task DeleteRoleAsync(Guid roleId, CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("RbacService.DeleteRole");
        activity?.SetTag("rbac.role.id", roleId.ToString());

        if (_roles.TryGetValue(roleId, out var existing) && existing.IsSystem)
            throw new InvalidOperationException("System roles cannot be deleted.");

        if (!_roles.TryRemove(roleId, out _))
            throw new KeyNotFoundException($"Role {roleId} not found.");

        // Remove all assignments for this role
        foreach (var kvp in _assignments)
        {
            if (kvp.Value.RoleId == roleId)
                _assignments.TryRemove(kvp.Key, out _);
        }

        OTel.RbacRoleChanges.Add(1);

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "rbac.role.deleted",
            "RbacService",
            Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["roleId"] = roleId.ToString()
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<RoleAssignment>> GetAssignmentsAsync(string? subjectId = null, CancellationToken ct = default)
    {
        IReadOnlyList<RoleAssignment> result = string.IsNullOrEmpty(subjectId)
            ? _assignments.Values.ToList().AsReadOnly()
            : _assignments.Values.Where(a => a.SubjectId == subjectId).ToList().AsReadOnly();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public async global::System.Threading.Tasks.Task<RoleAssignment> AssignRoleAsync(string subjectId, string subjectType, Guid roleId, string assignedBy, CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("RbacService.AssignRole");
        activity?.SetTag("rbac.subject.id", subjectId);
        activity?.SetTag("rbac.role.id", roleId.ToString());

        if (!_roles.ContainsKey(roleId))
            throw new KeyNotFoundException($"Role {roleId} not found.");

        var assignment = new RoleAssignment(Guid.NewGuid(), subjectId, subjectType, roleId, assignedBy, DateTimeOffset.UtcNow);
        _assignments[assignment.Id] = assignment;

        OTel.RbacRoleChanges.Add(1);

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "rbac.role.assigned",
            "RbacService",
            Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["assignmentId"] = assignment.Id.ToString(),
                ["subjectId"] = subjectId,
                ["subjectType"] = subjectType,
                ["roleId"] = roleId.ToString(),
                ["assignedBy"] = assignedBy
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);

        return assignment;
    }

    public async global::System.Threading.Tasks.Task RevokeRoleAsync(Guid assignmentId, CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("RbacService.RevokeRole");
        activity?.SetTag("rbac.assignment.id", assignmentId.ToString());

        if (!_assignments.TryRemove(assignmentId, out var removed))
            throw new KeyNotFoundException($"Assignment {assignmentId} not found.");

        OTel.RbacRoleChanges.Add(1);

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "rbac.role.revoked",
            "RbacService",
            Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["assignmentId"] = assignmentId.ToString(),
                ["subjectId"] = removed.SubjectId,
                ["roleId"] = removed.RoleId.ToString()
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<PermissionPolicy>> GetPoliciesAsync(CancellationToken ct = default)
    {
        IReadOnlyList<PermissionPolicy> result = _policies.Values.ToList().AsReadOnly();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public async global::System.Threading.Tasks.Task<PermissionPolicy> CreatePolicyAsync(string name, string description, IReadOnlyList<string> requiredPermissions, string resource, string effect, IReadOnlyDictionary<string, string> conditions, CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("RbacService.CreatePolicy");
        activity?.SetTag("rbac.policy.name", name);

        var policy = new PermissionPolicy(Guid.NewGuid(), name, description, requiredPermissions, resource, effect, conditions, true, DateTimeOffset.UtcNow);
        _policies[policy.Id] = policy;

        OTel.RbacPolicyChanges.Add(1);

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "rbac.policy.created",
            "RbacService",
            Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["policyId"] = policy.Id.ToString(),
                ["policyName"] = name,
                ["effect"] = effect
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);

        return policy;
    }

    public async global::System.Threading.Tasks.Task UpdatePolicyAsync(Guid policyId, bool isEnabled, CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("RbacService.UpdatePolicy");
        activity?.SetTag("rbac.policy.id", policyId.ToString());

        if (!_policies.TryGetValue(policyId, out var existing))
            throw new KeyNotFoundException($"Policy {policyId} not found.");

        _policies[policyId] = existing with { IsEnabled = isEnabled };

        OTel.RbacPolicyChanges.Add(1);

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "rbac.policy.updated",
            "RbacService",
            Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["policyId"] = policyId.ToString(),
                ["isEnabled"] = isEnabled.ToString()
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);
    }

    public async global::System.Threading.Tasks.Task DeletePolicyAsync(Guid policyId, CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("RbacService.DeletePolicy");
        activity?.SetTag("rbac.policy.id", policyId.ToString());

        if (!_policies.TryRemove(policyId, out _))
            throw new KeyNotFoundException($"Policy {policyId} not found.");

        OTel.RbacPolicyChanges.Add(1);

        await _eventBus.PublishAsync(new SystemEvent(
            Guid.NewGuid(),
            "rbac.policy.deleted",
            "RbacService",
            Guid.NewGuid(),
            new Dictionary<string, string>
            {
                ["policyId"] = policyId.ToString()
            }.AsReadOnly(),
            DateTimeOffset.UtcNow), ct);
    }

    public global::System.Threading.Tasks.Task<AccessDecision> EvaluateAccessAsync(string subjectId, string resource, string action, CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("RbacService.EvaluateAccess");
        activity?.SetTag("rbac.subject.id", subjectId);
        activity?.SetTag("rbac.resource", resource);
        activity?.SetTag("rbac.action", action);

        Interlocked.Increment(ref _accessChecks);
        OTel.RbacAccessChecks.Add(1);

        // Collect all permissions for this subject via role assignments
        var subjectAssignments = _assignments.Values.Where(a => a.SubjectId == subjectId).ToList();
        var effectivePermissions = new HashSet<string>();

        foreach (var assignment in subjectAssignments)
        {
            if (_roles.TryGetValue(assignment.RoleId, out var role))
            {
                foreach (var perm in role.Permissions)
                    effectivePermissions.Add(perm);
            }
        }

        // Evaluate against policies
        var matchedPolicies = new List<string>();
        bool? policyDecision = null;

        foreach (var policy in _policies.Values.Where(p => p.IsEnabled))
        {
            bool resourceMatches = string.Equals(policy.Resource, resource, StringComparison.OrdinalIgnoreCase)
                || policy.Resource == "*";

            if (!resourceMatches) continue;

            bool hasRequiredPermissions = policy.RequiredPermissions.All(rp => effectivePermissions.Contains(rp));

            if (hasRequiredPermissions)
            {
                matchedPolicies.Add(policy.Name);

                if (string.Equals(policy.Effect, "deny", StringComparison.OrdinalIgnoreCase))
                {
                    policyDecision = false;
                    break; // deny takes precedence
                }

                if (string.Equals(policy.Effect, "allow", StringComparison.OrdinalIgnoreCase))
                    policyDecision = true;
            }
        }

        // If no policy matched, fall back to permission check
        bool isAllowed;
        string reason;

        if (policyDecision.HasValue)
        {
            isAllowed = policyDecision.Value;
            reason = isAllowed
                ? $"Access granted by policies: {string.Join(", ", matchedPolicies)}"
                : $"Access denied by policy: {matchedPolicies.LastOrDefault() ?? "unknown"}";
        }
        else
        {
            // Check if the subject has the action as a permission directly
            isAllowed = effectivePermissions.Contains(action);
            reason = isAllowed
                ? $"Access granted via permission '{action}'"
                : $"Access denied: subject '{subjectId}' lacks permission '{action}'";
        }

        if (!isAllowed)
        {
            Interlocked.Increment(ref _accessDenials);
            OTel.RbacAccessDenials.Add(1);
        }

        var decision = new AccessDecision(
            isAllowed,
            subjectId,
            resource,
            action,
            reason,
            matchedPolicies.AsReadOnly(),
            DateTimeOffset.UtcNow);

        return global::System.Threading.Tasks.Task.FromResult(decision);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(string subjectId, CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("RbacService.GetEffectivePermissions");
        activity?.SetTag("rbac.subject.id", subjectId);

        var subjectAssignments = _assignments.Values.Where(a => a.SubjectId == subjectId).ToList();
        var effectivePermissions = new HashSet<string>();

        foreach (var assignment in subjectAssignments)
        {
            if (_roles.TryGetValue(assignment.RoleId, out var role))
            {
                foreach (var perm in role.Permissions)
                    effectivePermissions.Add(perm);
            }
        }

        IReadOnlyList<string> result = effectivePermissions.ToList().AsReadOnly();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public RbacStatus GetStatus()
    {
        return new RbacStatus(
            true,
            _roles.Count,
            _assignments.Count,
            _policies.Count,
            Interlocked.Read(ref _accessChecks),
            Interlocked.Read(ref _accessDenials),
            DateTimeOffset.UtcNow);
    }
}

using ArchonAI.Core.Models.Rbac;

namespace ArchonAI.Core.Interfaces;

public interface IRbacService
{
    global::System.Threading.Tasks.Task<IReadOnlyList<RbacRole>> GetRolesAsync(CancellationToken ct = default);
    global::System.Threading.Tasks.Task<RbacRole?> GetRoleAsync(Guid roleId, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<RbacRole> CreateRoleAsync(string name, string description, IReadOnlyList<string> permissions, CancellationToken ct = default);
    global::System.Threading.Tasks.Task UpdateRoleAsync(Guid roleId, string description, IReadOnlyList<string> permissions, CancellationToken ct = default);
    global::System.Threading.Tasks.Task DeleteRoleAsync(Guid roleId, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<RoleAssignment>> GetAssignmentsAsync(string? subjectId = null, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<RoleAssignment> AssignRoleAsync(string subjectId, string subjectType, Guid roleId, string assignedBy, CancellationToken ct = default);
    global::System.Threading.Tasks.Task RevokeRoleAsync(Guid assignmentId, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<PermissionPolicy>> GetPoliciesAsync(CancellationToken ct = default);
    global::System.Threading.Tasks.Task<PermissionPolicy> CreatePolicyAsync(string name, string description, IReadOnlyList<string> requiredPermissions, string resource, string effect, IReadOnlyDictionary<string, string> conditions, CancellationToken ct = default);
    global::System.Threading.Tasks.Task UpdatePolicyAsync(Guid policyId, bool isEnabled, CancellationToken ct = default);
    global::System.Threading.Tasks.Task DeletePolicyAsync(Guid policyId, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<AccessDecision> EvaluateAccessAsync(string subjectId, string resource, string action, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(string subjectId, CancellationToken ct = default);

    RbacStatus GetStatus();
}

namespace ArchonAI.Core.Models.Rbac;

public sealed record RbacRole(
    Guid Id,
    string Name,
    string Description,
    IReadOnlyList<string> Permissions,
    bool IsSystem,
    DateTimeOffset CreatedAtUtc);

public sealed record RbacPermission(
    string Name,
    string Resource,
    string Action,
    string Description);

public sealed record RoleAssignment(
    Guid Id,
    string SubjectId,
    string SubjectType,  // "user", "agent", "service"
    Guid RoleId,
    string AssignedBy,
    DateTimeOffset AssignedAtUtc);

public sealed record PermissionPolicy(
    Guid Id,
    string Name,
    string Description,
    IReadOnlyList<string> RequiredPermissions,
    string Resource,
    string Effect,  // "allow" or "deny"
    IReadOnlyDictionary<string, string> Conditions,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc);

public sealed record AccessDecision(
    bool IsAllowed,
    string SubjectId,
    string Resource,
    string Action,
    string Reason,
    IReadOnlyList<string> MatchedPolicies,
    DateTimeOffset EvaluatedAtUtc);

public sealed record RbacStatus(
    bool IsActive,
    int TotalRoles,
    int TotalAssignments,
    int TotalPolicies,
    long AccessChecks,
    long AccessDenials,
    DateTimeOffset StatusAsOfUtc);

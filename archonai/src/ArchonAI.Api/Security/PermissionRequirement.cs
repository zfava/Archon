using System.Security.Claims;
using ArchonAI.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace ArchonAI.Api.Security;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public PermissionRequirement(string permission) => Permission = permission;
}

public sealed class PermissionRequirementHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IRbacService _rbacService;

    // Permissions granted to each JWT role claim (mirrors RbacService seed data)
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> RolePermissions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Admin"] = new HashSet<string>
            {
                "agents:read", "agents:write", "agents:execute",
                "workflows:read", "workflows:write", "workflows:execute",
                "connectors:read", "connectors:write", "connectors:execute",
                "admin:read", "admin:write",
                "policy:read", "policy:write",
                "monitoring:read",
                "rbac:read", "rbac:write",
                "governance:read", "governance:write", "governance:approve",
            },
            ["Operator"] = new HashSet<string>
            {
                "agents:read", "agents:execute",
                "workflows:read", "workflows:execute",
                "connectors:read", "connectors:execute",
                "monitoring:read",
                "policy:read",
                "rbac:read",
                "governance:read",
            },
            ["Viewer"] = new HashSet<string>
            {
                "agents:read",
                "workflows:read",
                "connectors:read",
                "monitoring:read",
                "policy:read",
                "rbac:read",
                "governance:read",
            },
        };

    public PermissionRequirementHandler(IRbacService rbacService) => _rbacService = rbacService;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var userId = context.User?.FindFirst("sub")?.Value ?? context.User?.Identity?.Name;
        if (string.IsNullOrEmpty(userId)) return;

        // 1. Check JWT role claim for implicit permissions
        var roleClaim = context.User?.FindFirst(ClaimTypes.Role)?.Value
            ?? context.User?.FindFirst("role")?.Value;

        if (!string.IsNullOrEmpty(roleClaim)
            && RolePermissions.TryGetValue(roleClaim, out var rolePerms)
            && rolePerms.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
            return;
        }

        // 2. Fall back to RBAC store assignments (for custom roles / agent subjects)
        var decision = await _rbacService.EvaluateAccessAsync(
            userId, requirement.Permission.Split(':')[0], requirement.Permission);
        if (decision.IsAllowed)
            context.Succeed(requirement);
    }
}

/// <summary>
/// Enforces that the requesting user belongs to the same tenant as the resource.
/// Use as a requirement on routes that accept tenant-scoped resource IDs.
/// </summary>
public sealed class TenantMatchRequirement : IAuthorizationRequirement { }

public sealed class TenantMatchRequirementHandler : AuthorizationHandler<TenantMatchRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, TenantMatchRequirement requirement)
    {
        var tenantClaim = context.User?.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantClaim)) return Task.CompletedTask;

        // If the HTTP context carries a route/query tenantId, verify it matches
        if (context.Resource is HttpContext httpContext)
        {
            var routeTenantId = httpContext.Request.RouteValues["tenantId"]?.ToString()
                ?? httpContext.Request.Query["tenantId"].FirstOrDefault();

            if (routeTenantId is not null && !string.Equals(routeTenantId, tenantClaim, StringComparison.OrdinalIgnoreCase))
            {
                context.Fail(new AuthorizationFailureReason(this, "Cross-tenant access denied."));
                return Task.CompletedTask;
            }
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

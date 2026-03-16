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

    public PermissionRequirementHandler(IRbacService rbacService) => _rbacService = rbacService;

    protected override async global::System.Threading.Tasks.Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var userId = context.User?.FindFirst("sub")?.Value ?? context.User?.Identity?.Name;
        if (string.IsNullOrEmpty(userId)) return;

        var decision = await _rbacService.EvaluateAccessAsync(userId, requirement.Permission.Split(':')[0], requirement.Permission);
        if (decision.IsAllowed) context.Succeed(requirement);
    }
}

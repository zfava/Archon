using System.Text.Json;
using ArchonAI.Api.Dtos;
using ArchonAI.Core.Interfaces;

namespace ArchonAI.Api.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder v1)
    {
        var admin = v1.MapGroup("/admin")
            .RequireAuthorization("AdminOnly");

        MapAdminCoreEndpoints(admin);
        MapRbacEndpoints(admin);
        MapRuntimeHealthEndpoints(admin);
        MapSecurityEndpoints(admin);
        OidcEndpoints.MapTenantAuthEndpoints(admin);

        MapAuditEndpoints(v1);

        return v1;
    }

    private static void MapAdminCoreEndpoints(IEndpointRouteBuilder admin)
    {
        admin.MapGet("/status", (IAdminService adminService) => Results.Ok(adminService.GetStatus()));

        admin.MapGet("/agents", async (IAdminService adminService, CancellationToken ct) =>
        {
            var agents = await adminService.GetAgentsAsync(ct);
            return Results.Ok(agents);
        });

        admin.MapGet("/agents/{agentId:guid}", async (Guid agentId, IAdminService adminService, CancellationToken ct) =>
        {
            var agent = await adminService.GetAgentAsync(agentId, ct);
            return agent is null ? Results.NotFound() : Results.Ok(agent);
        });

        admin.MapPatch("/agents/{agentId:guid}/enabled", async (Guid agentId, AgentEnabledRequest enabledRequest, IAdminService adminService, CancellationToken ct) =>
        {
            await adminService.SetAgentEnabledAsync(agentId, enabledRequest.Enabled, ct);
            return Results.Ok(new { agentId, enabled = enabledRequest.Enabled });
        });

        admin.MapGet("/workflows", async (IAdminService adminService, CancellationToken ct) =>
        {
            var workflows = await adminService.GetWorkflowsAsync(ct);
            return Results.Ok(workflows);
        });

        admin.MapGet("/workflows/{workflowId:guid}", async (Guid workflowId, IAdminService adminService, CancellationToken ct) =>
        {
            var workflow = await adminService.GetWorkflowAsync(workflowId, ct);
            return workflow is null ? Results.NotFound() : Results.Ok(workflow);
        });

        admin.MapPost("/workflows/{workflowId:guid}/cancel", async (
            Guid workflowId,
            HttpContext ctx,
            IAdminService adminService,
            IGovernanceService gov,
            CancellationToken ct) =>
        {
            var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
            var userId = ctx.User?.FindFirst("sub")?.Value
                ?? ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (tenantId is null || userId is null) return Results.Unauthorized();

            if (await gov.RequiresApprovalAsync("workflow.cancel", ct))
            {
                var payload = JsonSerializer.Serialize(new { workflowId = workflowId.ToString() });
                var gate = await gov.RequestApprovalAsync(
                    "workflow.cancel", workflowId.ToString(), tenantId, userId,
                    "Workflow cancellation requested", payload, ct);
                return Results.Accepted($"/api/v1/governance/{gate.Id}", gate);
            }

            await adminService.CancelWorkflowAsync(workflowId, ct);
            return Results.Ok(new { workflowId, cancelled = true });
        });

        admin.MapGet("/policy", async (IAdminService adminService, CancellationToken ct) =>
        {
            var policy = await adminService.GetPolicyConfigAsync(ct);
            return Results.Ok(policy);
        });

        admin.MapPut("/policy", async (ArchonAI.Core.Models.Admin.PolicyConfiguration policy, IAdminService adminService, CancellationToken ct) =>
        {
            await adminService.UpdatePolicyConfigAsync(policy, ct);
            return Results.Ok(new { updated = true });
        });

        admin.MapGet("/monitoring", async (IAdminService adminService, CancellationToken ct) =>
        {
            var snapshot = await adminService.GetSystemSnapshotAsync(ct);
            return Results.Ok(snapshot);
        });
    }

    private static void MapRbacEndpoints(IEndpointRouteBuilder admin)
    {
        var rbac = admin.MapGroup("/rbac");

        rbac.MapGet("/status", (IRbacService rbacService) => Results.Ok(rbacService.GetStatus()));

        rbac.MapGet("/roles", async (IRbacService rbacService, CancellationToken ct) =>
        {
            var roles = await rbacService.GetRolesAsync(ct);
            return Results.Ok(roles);
        });

        rbac.MapGet("/roles/{roleId:guid}", async (Guid roleId, IRbacService rbacService, CancellationToken ct) =>
        {
            var role = await rbacService.GetRoleAsync(roleId, ct);
            return role is null ? Results.NotFound() : Results.Ok(role);
        });

        rbac.MapPost("/roles", async (CreateRoleRequest request, IRbacService rbacService, CancellationToken ct) =>
        {
            var role = await rbacService.CreateRoleAsync(request.Name, request.Description, request.Permissions, ct);
            return Results.Created($"/api/v1/admin/rbac/roles/{role.Id}", role);
        });

        rbac.MapPut("/roles/{roleId:guid}", async (Guid roleId, UpdateRoleRequest request, IRbacService rbacService, CancellationToken ct) =>
        {
            await rbacService.UpdateRoleAsync(roleId, request.Description, request.Permissions, ct);
            return Results.Ok(new { roleId, updated = true });
        });

        rbac.MapDelete("/roles/{roleId:guid}", async (Guid roleId, IRbacService rbacService, CancellationToken ct) =>
        {
            await rbacService.DeleteRoleAsync(roleId, ct);
            return Results.Ok(new { roleId, deleted = true });
        });

        rbac.MapGet("/assignments", async (string? subjectId, IRbacService rbacService, CancellationToken ct) =>
        {
            var assignments = await rbacService.GetAssignmentsAsync(subjectId, ct);
            return Results.Ok(assignments);
        });

        rbac.MapPost("/assignments", async (AssignRoleRequest request, IRbacService rbacService, CancellationToken ct) =>
        {
            var assignment = await rbacService.AssignRoleAsync(request.SubjectId, request.SubjectType, request.RoleId, request.AssignedBy, ct);
            return Results.Created($"/api/v1/admin/rbac/assignments/{assignment.Id}", assignment);
        });

        rbac.MapDelete("/assignments/{assignmentId:guid}", async (Guid assignmentId, IRbacService rbacService, CancellationToken ct) =>
        {
            await rbacService.RevokeRoleAsync(assignmentId, ct);
            return Results.Ok(new { assignmentId, revoked = true });
        });

        rbac.MapGet("/policies", async (IRbacService rbacService, CancellationToken ct) =>
        {
            var policies = await rbacService.GetPoliciesAsync(ct);
            return Results.Ok(policies);
        });

        rbac.MapPost("/policies", async (CreatePolicyRequest request, IRbacService rbacService, CancellationToken ct) =>
        {
            var policy = await rbacService.CreatePolicyAsync(request.Name, request.Description, request.RequiredPermissions, request.Resource, request.Effect, request.Conditions.AsReadOnly(), ct);
            return Results.Created($"/api/v1/admin/rbac/policies/{policy.Id}", policy);
        });

        rbac.MapMethods("/policies/{policyId:guid}", ["PATCH"], async (Guid policyId, UpdatePolicyEnabledRequest request, IRbacService rbacService, CancellationToken ct) =>
        {
            await rbacService.UpdatePolicyAsync(policyId, request.IsEnabled, ct);
            return Results.Ok(new { policyId, updated = true });
        });

        rbac.MapDelete("/policies/{policyId:guid}", async (Guid policyId, IRbacService rbacService, CancellationToken ct) =>
        {
            await rbacService.DeletePolicyAsync(policyId, ct);
            return Results.Ok(new { policyId, deleted = true });
        });

        rbac.MapPost("/evaluate", async (EvaluateAccessRequest request, IRbacService rbacService, CancellationToken ct) =>
        {
            var decision = await rbacService.EvaluateAccessAsync(request.SubjectId, request.Resource, request.Action, ct);
            return Results.Ok(decision);
        });

        rbac.MapGet("/permissions/{subjectId}", async (string subjectId, IRbacService rbacService, CancellationToken ct) =>
        {
            var permissions = await rbacService.GetEffectivePermissionsAsync(subjectId, ct);
            return Results.Ok(permissions);
        });
    }

    private static void MapRuntimeHealthEndpoints(IEndpointRouteBuilder admin)
    {
        var runtimeHealth = admin.MapGroup("/runtime/health");

        runtimeHealth.MapGet("/", async (IRuntimeHealthManager healthManager, CancellationToken ct) =>
        {
            var snapshot = await healthManager.GetHealthSnapshotAsync();
            return Results.Ok(snapshot);
        });

        runtimeHealth.MapGet("/policies", (IRuntimeHealthManager healthManager) =>
        {
            var policies = healthManager.GetRecoveryPolicies();
            return Results.Ok(policies);
        });

        runtimeHealth.MapGet("/recoveries", async (int? limit, IRuntimeHealthManager healthManager, CancellationToken ct) =>
        {
            var history = await healthManager.GetRecoveryHistoryAsync(limit ?? 50);
            return Results.Ok(history);
        });

        runtimeHealth.MapPost("/check", async (IRuntimeHealthManager healthManager, CancellationToken ct) =>
        {
            await healthManager.RunHealthCheckAsync(ct);
            var snapshot = await healthManager.GetHealthSnapshotAsync();
            return Results.Ok(snapshot);
        });
    }

    private static void MapSecurityEndpoints(IEndpointRouteBuilder admin)
    {
        var security = admin.MapGroup("/security");

        security.MapGet("/metrics", (ISecurityPolicyEngine securityEngine) =>
            Results.Ok(securityEngine.GetMetrics()));

        security.MapGet("/policies", async (string? category, ISecurityPolicyEngine securityEngine, CancellationToken ct) =>
        {
            var policies = await securityEngine.GetPoliciesAsync(category, ct);
            return Results.Ok(policies);
        });

        security.MapPost("/policies", async (AddSecurityPolicyRequest request, ISecurityPolicyEngine securityEngine, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var policy = new ArchonAI.Core.Models.Governance.SecurityPolicy(
                Id: Guid.NewGuid(),
                Name: request.Name,
                Category: request.Category,
                Rule: new ArchonAI.Core.Models.Governance.SecurityPolicyRule(
                    request.RuleType,
                    request.AllowedValues ?? Array.Empty<string>(),
                    request.DeniedValues ?? Array.Empty<string>(),
                    request.Limits ?? new Dictionary<string, string>()),
                IsEnabled: true,
                CreatedAtUtc: now,
                UpdatedAtUtc: now);
            await securityEngine.AddPolicyAsync(policy, ct);
            return Results.Created($"/api/v1/admin/security/policies/{policy.Id}", policy);
        });

        security.MapDelete("/policies/{policyId:guid}", async (Guid policyId, ISecurityPolicyEngine securityEngine, CancellationToken ct) =>
        {
            await securityEngine.RemovePolicyAsync(policyId, ct);
            return Results.Ok(new { policyId, removed = true });
        });

        security.MapPost("/evaluate/data-access", async (EvaluateDataAccessRequest request, ISecurityPolicyEngine securityEngine, CancellationToken ct) =>
        {
            var result = await securityEngine.EvaluateDataAccessAsync(request.SubjectId, request.ResourceType, request.Action, ct);
            return Results.Ok(result);
        });

        security.MapPost("/evaluate/workflow-limits", async (EvaluateWorkflowLimitsRequest request, ISecurityPolicyEngine securityEngine, CancellationToken ct) =>
        {
            var result = await securityEngine.EvaluateWorkflowLimitsAsync(request.WorkflowId, request.StepCount, request.ConcurrentAgents, ct);
            return Results.Ok(result);
        });
    }

    private static void MapAuditEndpoints(IEndpointRouteBuilder v1)
    {
        var audit = v1.MapGroup("/audit")
            .RequireAuthorization("OperatorOrAdmin");

        audit.MapGet("/status", (IAuditLogService auditService) =>
        {
            var status = auditService.GetStatus();
            return Results.Ok(status);
        });

        audit.MapGet("/entries", async (string? category, string? subjectId, string? resourceType,
            DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int? offset, int? limit,
            IAuditLogService auditService, CancellationToken ct) =>
        {
            var result = await auditService.QueryAsync(category, subjectId, resourceType, fromUtc, toUtc, offset ?? 0, limit ?? 100, ct);
            return Results.Ok(result);
        });

        audit.MapGet("/entries/{entryId:guid}", async (Guid entryId, IAuditLogService auditService, CancellationToken ct) =>
        {
            var entry = await auditService.GetEntryAsync(entryId, ct);
            return entry is null ? Results.NotFound() : Results.Ok(entry);
        });

        audit.MapPost("/record", async (AuditRecordRequest request, IAuditLogService auditService, CancellationToken ct) =>
        {
            var entry = await auditService.RecordAsync(
                request.EventType, request.Category, request.Source,
                request.SubjectId, request.SubjectType, request.Action,
                request.ResourceType, request.ResourceId, request.Description,
                request.Metadata, ct);
            return Results.Created($"/api/v1/audit/entries/{entry.Id}", entry);
        }).RequireAuthorization("AdminOnly");

        audit.MapPost("/verify", async (AuditVerifyRequest? request, IAuditLogService auditService, CancellationToken ct) =>
        {
            var isValid = await auditService.VerifyIntegrityAsync(request?.FromEntryId, ct);
            return Results.Ok(new { integrityValid = isValid, verifiedAtUtc = DateTimeOffset.UtcNow });
        });
    }
}

using System.Security.Claims;
using ArchonAI.Api.Dtos;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.ActionSafety;
using ArchonAI.Core.Models.Governance;

namespace ArchonAI.Api.Endpoints;

public static class GovernanceEndpoints
{
    public static IEndpointRouteBuilder MapGovernanceEndpoints(this IEndpointRouteBuilder v1)
    {
        MapGovernanceCrudEndpoints(v1);
        MapTrustTierEndpoints(v1);
        MapPermissionsEndpoints(v1);
        MapActionSafetyEndpoints(v1);
        return v1;
    }

    private static void MapGovernanceCrudEndpoints(IEndpointRouteBuilder v1)
    {
        var governance = v1.MapGroup("/governance")
            .WithTags("governance");

        governance.MapGet("/policies", async (
            IGovernanceService gov,
            CancellationToken ct) =>
        {
            var policies = await gov.ListApprovalPoliciesAsync(ct);
            return Results.Ok(policies);
        }).RequireAuthorization("GovernanceRead");

        governance.MapPost("/policies", async (
            CreateApprovalPolicyRequest req,
            IGovernanceService gov,
            CancellationToken ct) =>
        {
            var policy = await gov.CreateApprovalPolicyAsync(
                req.ActionType, req.Description, req.RequiredApproverRole,
                req.RequireSeparationOfDuties, ct);
            return Results.Created($"/api/v1/governance/policies/{policy.Id}", policy);
        }).RequireAuthorization("GovernanceWrite");

        governance.MapPost("/request", async (
            RequestApprovalRequest req,
            HttpContext ctx,
            IGovernanceService gov,
            CancellationToken ct) =>
        {
            var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
            var userId = ctx.User?.FindFirst("sub")?.Value;
            if (tenantId is null || userId is null) return Results.Unauthorized();

            var gate = await gov.RequestApprovalAsync(
                req.ActionType, req.ResourceId, tenantId, userId, req.Justification, ct: ct);
            return Results.Ok(gate);
        }).RequireAuthorization("OperatorOrAdmin");

        governance.MapGet("/pending", async (
            HttpContext ctx,
            IGovernanceService gov,
            CancellationToken ct) =>
        {
            var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantId is null) return Results.Unauthorized();

            var pending = await gov.ListPendingApprovalsAsync(tenantId, ct);
            return Results.Ok(pending);
        }).RequireAuthorization("GovernanceRead");

        governance.MapGet("/{gateId:guid}", async (
            Guid gateId,
            HttpContext ctx,
            IGovernanceService gov,
            CancellationToken ct) =>
        {
            var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantId is null) return Results.Unauthorized();

            var gate = await gov.GetApprovalAsync(gateId, tenantId, ct);
            return gate is null ? Results.NotFound() : Results.Ok(gate);
        }).RequireAuthorization("GovernanceRead");

        governance.MapPost("/{gateId:guid}/review", async (
            Guid gateId,
            ReviewApprovalRequest req,
            HttpContext ctx,
            IGovernanceService gov,
            IGatedActionExecutor executor,
            CancellationToken ct) =>
        {
            var reviewerId = ctx.User?.FindFirst("sub")?.Value
                ?? ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
            var reviewerRole = ctx.User?.FindFirst(ClaimTypes.Role)?.Value
                ?? ctx.User?.FindFirst("role")?.Value;
            if (reviewerId is null || tenantId is null) return Results.Unauthorized();

            try
            {
                var gate = await gov.ReviewApprovalAsync(
                    gateId, tenantId, reviewerId, reviewerRole ?? "Viewer", req.Approve, req.Notes, ct);

                // Execute the gated action on approval
                if (gate.Status == ApprovalStatus.Approved && gate.ActionPayload is not null)
                {
                    try
                    {
                        var result = await executor.ExecuteAsync(gate, ct);
                        gate = await gov.RecordExecutionResultAsync(
                            gate.Id,
                            result.Success ? GateExecutionStatus.Succeeded : GateExecutionStatus.Failed,
                            result.Error, ct);
                    }
                    catch (Exception ex)
                    {
                        gate = await gov.RecordExecutionResultAsync(
                            gate.Id, GateExecutionStatus.Failed, ex.Message, ct);
                    }
                }

                return Results.Ok(gate);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 403);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        }).RequireAuthorization("GovernanceApprove");

        governance.MapGet("/approval-required/{actionType}", async (
            string actionType,
            IGovernanceService gov,
            CancellationToken ct) =>
        {
            var required = await gov.RequiresApprovalAsync(actionType, ct);
            return Results.Ok(new { actionType, requiresApproval = required });
        }).RequireAuthorization("GovernanceRead");

        governance.MapGet("/history", async (
            string? actionType,
            int? limit,
            HttpContext ctx,
            IGovernanceService gov,
            CancellationToken ct) =>
        {
            var callerTenant = ctx.User?.FindFirst("tenant_id")?.Value;
            if (callerTenant is null) return Results.Unauthorized();

            var history = await gov.GetApprovalHistoryAsync(callerTenant, actionType, limit ?? 50, ct);
            return Results.Ok(history);
        }).RequireAuthorization("GovernanceRead");
    }

    private static void MapTrustTierEndpoints(IEndpointRouteBuilder v1)
    {
        var trustTiers = v1.MapGroup("/trust-tiers")
            .WithTags("trust-tiers");

        trustTiers.MapGet("/policies", async (
            HttpContext ctx,
            ITrustTierService trustSvc,
            CancellationToken ct) =>
        {
            var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantId is null) return Results.Unauthorized();

            var policies = await trustSvc.ListPoliciesAsync(tenantId, ct);
            return Results.Ok(policies);
        }).RequireAuthorization("GovernanceRead");

        trustTiers.MapPost("/policies", async (
            SetTrustTierPolicyRequest req,
            HttpContext ctx,
            ITrustTierService trustSvc,
            CancellationToken ct) =>
        {
            var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
            var userId = ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            if (tenantId is null) return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(req.ActionScope))
                return Results.BadRequest(new { error = "ActionScope is required." });

            if (!Enum.TryParse<ExecutionTrustTier>(req.MaxTier, true, out var maxTier))
                return Results.BadRequest(new { error = $"Invalid tier: {req.MaxTier}." });

            var now = DateTimeOffset.UtcNow;
            var policy = new TrustTierPolicy(
                Id: req.Id ?? Guid.NewGuid(),
                TenantId: tenantId,
                ActionScope: req.ActionScope,
                MaxTier: maxTier,
                ConfidenceThreshold: req.ConfidenceThreshold,
                ValueCeiling: req.ValueCeiling,
                RequireReversible: req.RequireReversible ?? false,
                Description: req.Description,
                IsEnabled: req.IsEnabled ?? true,
                CreatedBy: userId,
                CreatedAtUtc: now,
                UpdatedAtUtc: now);

            var created = await trustSvc.SetPolicyAsync(policy, ct);
            return Results.Created($"/api/v1/trust-tiers/policies/{created.Id}", created);
        }).RequireAuthorization("GovernanceWrite");

        trustTiers.MapDelete("/policies/{policyId:guid}", async (
            Guid policyId,
            HttpContext ctx,
            ITrustTierService trustSvc,
            CancellationToken ct) =>
        {
            var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantId is null) return Results.Unauthorized();

            var deleted = await trustSvc.DeletePolicyAsync(policyId, tenantId, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).RequireAuthorization("GovernanceWrite");

        trustTiers.MapPost("/evaluate", async (
            EvaluateTrustTierRequest req,
            HttpContext ctx,
            ITrustTierService trustSvc,
            CancellationToken ct) =>
        {
            var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantId is null) return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(req.ActionScope))
                return Results.BadRequest(new { error = "ActionScope is required." });

            if (!Enum.TryParse<ExecutionTrustTier>(req.RequestedTier, true, out var requestedTier))
                return Results.BadRequest(new { error = $"Invalid tier: {req.RequestedTier}." });

            var evaluation = await trustSvc.EvaluateAsync(
                tenantId, req.ActionScope, requestedTier,
                req.Confidence, req.Value, req.Reversible, ct);
            return Results.Ok(evaluation);
        }).RequireAuthorization("GovernanceRead");

        trustTiers.MapGet("/map", async (
            HttpContext ctx,
            ITrustTierService trustSvc,
            CancellationToken ct) =>
        {
            var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantId is null) return Results.Unauthorized();

            var map = await trustSvc.GetTierMapAsync(tenantId, ct);
            return Results.Ok(map);
        }).RequireAuthorization("GovernanceRead");

        trustTiers.MapGet("/effective/{actionScope}", async (
            string actionScope,
            HttpContext ctx,
            ITrustTierService trustSvc,
            CancellationToken ct) =>
        {
            var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantId is null) return Results.Unauthorized();

            var tier = await trustSvc.GetEffectiveTierAsync(tenantId, actionScope, ct);
            return Results.Ok(new { actionScope, effectiveTier = tier.ToString(), tierLevel = (int)tier });
        }).RequireAuthorization("GovernanceRead");
    }

    private static void MapPermissionsEndpoints(IEndpointRouteBuilder v1)
    {
        v1.MapGet("/auth/permissions", async (
            HttpContext ctx,
            IRbacService rbac,
            CancellationToken ct) =>
        {
            var userId = ctx.User?.FindFirst("sub")?.Value;
            if (userId is null) return Results.Unauthorized();

            var roleClaim = ctx.User?.FindFirst(ClaimTypes.Role)?.Value
                ?? ctx.User?.FindFirst("role")?.Value;

            var storePerms = await rbac.GetEffectivePermissionsAsync(userId, ct);

            // Merge JWT role permissions
            var all = new HashSet<string>(storePerms);
            if (roleClaim is not null)
            {
                var rolePerms = roleClaim switch
                {
                    "Admin" => new[] { "agents:read", "agents:write", "agents:execute", "workflows:read", "workflows:write", "workflows:execute", "connectors:read", "connectors:write", "connectors:execute", "admin:read", "admin:write", "policy:read", "policy:write", "monitoring:read", "rbac:read", "rbac:write", "governance:read", "governance:write", "governance:approve" },
                    "Operator" => new[] { "agents:read", "agents:execute", "workflows:read", "workflows:execute", "connectors:read", "connectors:execute", "monitoring:read", "policy:read", "rbac:read", "governance:read" },
                    "Viewer" => new[] { "agents:read", "workflows:read", "connectors:read", "monitoring:read", "policy:read", "rbac:read", "governance:read" },
                    _ => Array.Empty<string>()
                };
                foreach (var p in rolePerms) all.Add(p);
            }

            return Results.Ok(new { userId, role = roleClaim, permissions = all.Order().ToList() });
        }).RequireAuthorization();
    }

    private static void MapActionSafetyEndpoints(IEndpointRouteBuilder v1)
    {
        var actionSafety = v1.MapGroup("/action-safety")
            .WithTags("action-safety");

        actionSafety.MapGet("/classifications", async (
            IActionSafetyService safetySvc,
            CancellationToken ct) =>
        {
            var list = await safetySvc.ListClassificationsAsync(ct);
            return Results.Ok(list);
        }).RequireAuthorization("GovernanceRead");

        actionSafety.MapGet("/classifications/{actionType}", async (
            string actionType,
            IActionSafetyService safetySvc,
            CancellationToken ct) =>
        {
            var classification = await safetySvc.GetClassificationAsync(actionType, ct);
            return Results.Ok(classification);
        }).RequireAuthorization("GovernanceRead");

        actionSafety.MapPut("/classifications", async (
            SetSafetyClassificationRequest req,
            HttpContext ctx,
            IActionSafetyService safetySvc,
            CancellationToken ct) =>
        {
            var userId = ctx.User?.FindFirst("sub")?.Value ?? "unknown";

            if (!Enum.TryParse<ReversibilityLevel>(req.Reversibility, true, out var reversibility))
                return Results.BadRequest(new { error = $"Invalid reversibility: {req.Reversibility}." });
            if (!Enum.TryParse<RollbackStrategy>(req.RollbackStrategy, true, out var strategy))
                return Results.BadRequest(new { error = $"Invalid rollback strategy: {req.RollbackStrategy}." });

            TimeSpan? window = req.RollbackWindowMinutes.HasValue
                ? TimeSpan.FromMinutes(req.RollbackWindowMinutes.Value)
                : null;

            var classification = new ActionSafetyClassification(
                Id: Guid.NewGuid(),
                ActionType: req.ActionType,
                Reversibility: reversibility,
                RollbackSupported: req.RollbackSupported,
                RollbackStrategy: strategy,
                RollbackWindow: window,
                CompensationDescription: req.CompensationDescription,
                OperatorNotes: req.OperatorNotes,
                ClassifiedBy: userId,
                ClassifiedAtUtc: DateTimeOffset.UtcNow);

            var result = await safetySvc.SetClassificationAsync(classification, ct);
            return Results.Ok(result);
        }).RequireAuthorization("OperatorOrAdmin");

        actionSafety.MapPost("/actions", async (
            RecordGovernedActionRequest req,
            HttpContext ctx,
            IActionSafetyService safetySvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            var userId = ctx.User?.FindFirst("sub")?.Value ?? "unknown";
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var classification = await safetySvc.GetClassificationAsync(req.ActionType, ct);

            var action = new GovernedActionRecord(
                Id: Guid.NewGuid(),
                TenantId: tenantId,
                DecisionId: req.DecisionId,
                WorkflowId: req.WorkflowId,
                ApprovalGateId: req.ApprovalGateId,
                ActionType: req.ActionType,
                Description: req.Description,
                SafetyClassification: classification,
                Status: GovernedActionStatus.Executed,
                ExecutedBy: userId,
                ExecutedAtUtc: DateTimeOffset.UtcNow,
                RollbackHistory: Array.Empty<RollbackAttempt>(),
                CompensationOutcome: null,
                UpdatedAtUtc: DateTimeOffset.UtcNow);

            var created = await safetySvc.RecordActionAsync(action, ct);
            return Results.Created($"/api/v1/action-safety/actions/{created.Id}", created);
        }).RequireAuthorization("OperatorOrAdmin");

        actionSafety.MapGet("/actions", async (
            int? limit,
            HttpContext ctx,
            IActionSafetyService safetySvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var actions = await safetySvc.ListActionsAsync(tenantId, limit ?? 50, ct);
            return Results.Ok(actions);
        }).RequireAuthorization("GovernanceRead");

        actionSafety.MapGet("/actions/{actionId:guid}", async (
            Guid actionId,
            IActionSafetyService safetySvc,
            CancellationToken ct) =>
        {
            var action = await safetySvc.GetActionAsync(actionId, ct);
            return action is null ? Results.NotFound() : Results.Ok(action);
        }).RequireAuthorization("GovernanceRead");

        actionSafety.MapPost("/actions/{actionId:guid}/rollback", async (
            Guid actionId,
            HttpContext ctx,
            IActionSafetyService safetySvc,
            CancellationToken ct) =>
        {
            var userId = ctx.User?.FindFirst("sub")?.Value ?? "unknown";
            try
            {
                var result = await safetySvc.AttemptRollbackAsync(actionId, userId, ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = "Governed action not found." });
            }
        }).RequireAuthorization("OperatorOrAdmin");

        actionSafety.MapGet("/summary", async (
            HttpContext ctx,
            IActionSafetyService safetySvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var summary = await safetySvc.GetRollbackSummaryAsync(tenantId, ct);
            return Results.Ok(summary);
        }).RequireAuthorization("GovernanceRead");
    }
}

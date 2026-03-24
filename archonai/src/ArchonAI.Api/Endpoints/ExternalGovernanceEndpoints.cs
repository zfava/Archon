using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArchonAI.Api.Dtos;
using ArchonAI.Api.Security;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Governance;
using ArchonAI.Core.Models.Policy;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Api.Endpoints;

public static class ExternalGovernanceEndpoints
{
    // In-memory evaluation store — production would use durable persistence
    private static readonly ConcurrentDictionary<Guid, ExternalEvaluationResponse> _evaluations = new();

    public static IEndpointRouteBuilder MapExternalGovernanceEndpoints(this IEndpointRouteBuilder app)
    {
        var external = app.MapGroup("/api/v1/external/governance")
            .WithTags("external-governance")
            .RequireAuthorization(policy =>
            {
                policy.AuthenticationSchemes = [ExternalApiKeyAuthHandler.SchemeName];
                policy.RequireAuthenticatedUser();
            })
            .RequireRateLimiting("external-api");

        MapEvaluateEndpoint(external);
        MapGetEvaluationEndpoint(external);
        MapApprovalStatusEndpoint(external);
        MapTrustTierEndpoint(external);

        return app;
    }

    private static void MapEvaluateEndpoint(RouteGroupBuilder group)
    {
        group.MapPost("/evaluate", async (
            ExternalEvaluationRequest req,
            HttpContext ctx,
            IPolicyEngine policyEngine,
            ITrustTierService trustTierService,
            IGovernanceService governanceService,
            IEventBus eventBus,
            ISecretProvider secretProvider,
            CancellationToken ct) =>
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(req.TaskDescription))
                return Results.BadRequest(new { error = "taskDescription is required." });
            if (string.IsNullOrWhiteSpace(req.RequiredCapability))
                return Results.BadRequest(new { error = "requiredCapability is required." });
            if (string.IsNullOrWhiteSpace(req.ActionScope))
                return Results.BadRequest(new { error = "actionScope is required." });
            if (string.IsNullOrWhiteSpace(req.CallerIdentity))
                return Results.BadRequest(new { error = "callerIdentity is required." });
            if (string.IsNullOrWhiteSpace(req.OrganizationId))
                return Results.BadRequest(new { error = "organizationId is required." });

            var orgId = ctx.User?.FindFirst("org-id")?.Value ?? req.OrganizationId;
            var evaluationId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            var objectiveId = Guid.NewGuid();

            // Synthesize Agent from external caller
            var agent = new Agent(
                Id: Guid.NewGuid(),
                Name: $"external:{req.CallerIdentity}",
                Version: "1.0",
                Capabilities: [new AgentCapability(req.RequiredCapability, "External capability", "external", "1.0")],
                IsEnabled: true,
                RegisteredAtUtc: DateTimeOffset.UtcNow);

            // Synthesize Task from request
            var inputs = new Dictionary<string, string>
            {
                ["description"] = req.TaskDescription,
                ["actionScope"] = req.ActionScope,
            };
            if (req.ContextMetadata is not null)
            {
                foreach (var kv in req.ContextMetadata)
                    inputs[kv.Key] = kv.Value;
            }

            var task = new CoreTask(
                Id: taskId,
                ObjectiveId: objectiveId,
                Order: 1,
                Name: req.TaskDescription.Length > 100 ? req.TaskDescription[..100] : req.TaskDescription,
                Description: req.TaskDescription,
                RequiredCapability: req.RequiredCapability,
                Inputs: inputs.AsReadOnly(),
                CreatedAtUtc: DateTimeOffset.UtcNow,
                StartedAtUtc: null,
                CompletedAtUtc: null);

            // Synthesize ExecutionContext
            var metadata = new Dictionary<string, string>
            {
                ["tenantId"] = orgId,
                ["callerIdentity"] = req.CallerIdentity,
                ["source"] = "external-api",
            };
            if (req.ContextMetadata is not null)
            {
                foreach (var kv in req.ContextMetadata)
                    metadata.TryAdd(kv.Key, kv.Value);
            }

            var executionContext = new CoreExecutionContext(
                CorrelationId: evaluationId,
                ObjectiveId: objectiveId,
                TaskId: taskId,
                TenantId: orgId,
                Metadata: metadata.AsReadOnly(),
                RequestedAtUtc: DateTimeOffset.UtcNow);

            // Call PolicyEngine
            PolicyDecision policyDecision = await policyEngine.EvaluateAsync(agent, task, executionContext, ct);

            // Call TrustTierService — determine effective tier for the action scope
            var effectiveTier = await trustTierService.GetEffectiveTierAsync(orgId, req.ActionScope, ct);

            // Determine decision
            string decision;
            Guid? approvalGateId = null;

            if (!policyDecision.IsAllowed && policyDecision.GuardrailViolations.Contains("forbidden-capability"))
            {
                decision = "deny";
            }
            else if (policyDecision.RequiresApproval || policyDecision.RiskScore >= 50)
            {
                decision = "require-approval";

                // Create an approval gate
                bool requiresApproval = await governanceService.RequiresApprovalAsync(req.ActionScope, ct);
                var gate = await governanceService.RequestApprovalAsync(
                    actionType: req.ActionScope,
                    resourceId: evaluationId.ToString(),
                    tenantId: orgId,
                    requestedBy: req.CallerIdentity,
                    justification: req.TaskDescription,
                    ct: ct);
                approvalGateId = gate.Id;
            }
            else if (policyDecision.IsAllowed)
            {
                decision = "allow";
            }
            else
            {
                decision = "deny";
            }

            var guardrailFindings = new List<string>(policyDecision.GuardrailViolations);
            if (policyDecision.RiskScore >= 50 && !guardrailFindings.Contains("value-exceeds-auto-execute-threshold"))
                guardrailFindings.Add("value-exceeds-auto-execute-threshold");

            var evaluatedAt = DateTimeOffset.UtcNow;

            // Sign the evaluation
            var signingKey = secretProvider.GetSecret("external-api-signing-key") ?? "default-signing-key-for-dev";
            var signedDigest = SignEvaluation(evaluationId, decision, policyDecision.RiskScore, evaluatedAt, signingKey);

            var response = new ExternalEvaluationResponse(
                Decision: decision,
                Confidence: Math.Clamp(policyDecision.ConfidenceScore, 0, 1),
                TrustTierApplied: effectiveTier.ToString(),
                RiskScore: policyDecision.RiskScore,
                Violations: policyDecision.GuardrailViolations,
                GuardrailFindings: guardrailFindings,
                ApprovalGateId: approvalGateId,
                EvaluationId: evaluationId,
                EvaluatedAt: evaluatedAt,
                SignedDigest: signedDigest);

            _evaluations[evaluationId] = response;

            // Publish audit event
            _ = eventBus.PublishAsync(new SystemEvent(
                Guid.NewGuid(),
                "external.governance.evaluation",
                "ExternalGovernanceEndpoints",
                evaluationId,
                new Dictionary<string, string>
                {
                    ["evaluationId"] = evaluationId.ToString(),
                    ["decision"] = decision,
                    ["riskScore"] = policyDecision.RiskScore.ToString(),
                    ["callerIdentity"] = req.CallerIdentity,
                    ["organizationId"] = orgId,
                    ["actionScope"] = req.ActionScope,
                }.AsReadOnly(),
                evaluatedAt), ct);

            return Results.Ok(response);
        });
    }

    private static void MapGetEvaluationEndpoint(RouteGroupBuilder group)
    {
        group.MapGet("/evaluation/{evaluationId:guid}", (
            Guid evaluationId,
            HttpContext ctx) =>
        {
            if (!_evaluations.TryGetValue(evaluationId, out var evaluation))
                return Results.NotFound(new { error = "Evaluation not found." });

            return Results.Ok(evaluation);
        });
    }

    private static void MapApprovalStatusEndpoint(RouteGroupBuilder group)
    {
        group.MapPost("/approval/{gateId:guid}/status", async (
            Guid gateId,
            ExternalApprovalStatusRequest req,
            HttpContext ctx,
            IGovernanceService governanceService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.CallerIdentity))
                return Results.BadRequest(new { error = "callerIdentity is required." });

            var orgId = ctx.User?.FindFirst("org-id")?.Value;
            if (orgId is null) return Results.Unauthorized();

            var gate = await governanceService.GetApprovalAsync(gateId, orgId, ct);
            if (gate is null)
                return Results.NotFound(new { error = "Approval gate not found." });

            return Results.Ok(new ExternalApprovalStatusResponse(
                GateId: gate.Id,
                Status: gate.Status.ToString(),
                ReviewedBy: gate.ReviewedBy,
                ReviewNotes: gate.ReviewNotes,
                ReviewedAtUtc: gate.ReviewedAtUtc));
        });
    }

    private static void MapTrustTierEndpoint(RouteGroupBuilder group)
    {
        group.MapGet("/trust-tier/{actionScope}", async (
            string actionScope,
            HttpContext ctx,
            ITrustTierService trustTierService,
            CancellationToken ct) =>
        {
            var orgId = ctx.User?.FindFirst("org-id")?.Value;
            if (orgId is null) return Results.Unauthorized();

            var tier = await trustTierService.GetEffectiveTierAsync(orgId, actionScope, ct);
            return Results.Ok(new ExternalTrustTierResponse(
                ActionScope: actionScope,
                EffectiveTier: tier.ToString(),
                TierLevel: (int)tier));
        });
    }

    internal static string SignEvaluation(
        Guid evaluationId, string decision, double riskScore,
        DateTimeOffset evaluatedAt, string signingKey)
    {
        var payload = $"{evaluationId}:{decision}:{riskScore:F2}:{evaluatedAt:O}";
        var keyBytes = Encoding.UTF8.GetBytes(signingKey);
        var dataBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, dataBytes);
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // Exposed for testing
    internal static void ClearEvaluations() => _evaluations.Clear();
}

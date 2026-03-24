using System.Security.Claims;
using ArchonAI.Api.Dtos;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Decisions;
using ArchonAI.Core.Models.ProofAnalytics;

namespace ArchonAI.Api.Endpoints;

public static class DecisionEndpoints
{
    public static IEndpointRouteBuilder MapDecisionEndpoints(this IEndpointRouteBuilder v1)
    {
        MapDecisionCrudEndpoints(v1);
        MapOutcomesEndpoints(v1);
        MapProofAnalyticsEndpoints(v1);
        return v1;
    }

    private static void MapDecisionCrudEndpoints(IEndpointRouteBuilder v1)
    {
        var decisions = v1.MapGroup("/decisions")
            .RequireAuthorization("OperatorOrAdmin");

        decisions.MapPost("/", async (
            CreateDecisionRequest req,
            IDecisionService decisionService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Domain))
                return Results.BadRequest(new { error = "Title and domain are required." });

            var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
            var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";

            var alternatives = req.Alternatives?.Select(a => new DecisionAlternative(
                Guid.NewGuid().ToString("N")[..8], a.Title, a.Rationale,
                a.Pros ?? Array.Empty<string>(), a.Cons ?? Array.Empty<string>(),
                a.EstimatedConfidence, a.EstimatedValue)).ToList()
                ?? new List<DecisionAlternative>();

            var decision = new DecisionRecord(
                Id: Guid.NewGuid(),
                TenantId: tenantId,
                Title: req.Title,
                Domain: req.Domain,
                Objective: req.Objective ?? string.Empty,
                Constraints: req.Constraints ?? Array.Empty<string>(),
                Assumptions: req.Assumptions ?? Array.Empty<string>(),
                Alternatives: alternatives,
                RecommendedOptionId: req.RecommendedOptionId ?? string.Empty,
                Confidence: Math.Clamp(req.Confidence ?? 0.0, 0.0, 1.0),
                Reversibility: Enum.TryParse<DecisionReversibility>(req.Reversibility, true, out var rev) ? rev : DecisionReversibility.PartiallyReversible,
                RiskLevel: Enum.TryParse<DecisionRiskLevel>(req.RiskLevel, true, out var risk) ? risk : DecisionRiskLevel.Medium,
                ExpectedValue: req.ExpectedValue,
                RequiresApproval: req.RequiresApproval ?? (risk >= DecisionRiskLevel.High),
                LinkedArtifacts: Array.Empty<DecisionLink>(),
                Status: DecisionStatus.Draft,
                CreatedBy: userId,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                UpdatedAtUtc: DateTimeOffset.UtcNow);

            var created = await decisionService.CreateAsync(decision, ct);
            return Results.Created($"/api/v1/decisions/{created.Id}", created);
        });

        decisions.MapGet("/", async (
            IDecisionService decisionService,
            HttpContext httpContext,
            string? domain,
            string? status,
            int? limit,
            CancellationToken ct) =>
        {
            var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
            var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;
            var parsedStatus = Enum.TryParse<DecisionStatus>(status, true, out var s) ? s : (DecisionStatus?)null;

            var results = await decisionService.ListAsync(tenantId, domain, parsedStatus, limit ?? 50, ct);
            return Results.Ok(results);
        });

        decisions.MapGet("/{decisionId:guid}", async (
            Guid decisionId,
            IDecisionService decisionService,
            CancellationToken ct) =>
        {
            var decision = await decisionService.GetAsync(decisionId, ct);
            return decision is null ? Results.NotFound() : Results.Ok(decision);
        });

        decisions.MapPut("/{decisionId:guid}/status", async (
            Guid decisionId,
            UpdateDecisionStatusRequest req,
            IDecisionService decisionService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Status))
                return Results.BadRequest(new { error = "Status is required." });

            if (!Enum.TryParse<DecisionStatus>(req.Status, true, out var newStatus))
                return Results.BadRequest(new { error = $"Invalid status: {req.Status}." });

            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            var updated = await decisionService.UpdateStatusAsync(decisionId, newStatus, userId, req.Detail, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        decisions.MapPost("/{decisionId:guid}/links", async (
            Guid decisionId,
            CreateDecisionLinkRequest req,
            IDecisionService decisionService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.ArtifactType) || string.IsNullOrWhiteSpace(req.ArtifactId))
                return Results.BadRequest(new { error = "ArtifactType and ArtifactId are required." });

            var link = new DecisionLink(req.ArtifactType, req.ArtifactId,
                req.Description ?? string.Empty, DateTimeOffset.UtcNow);

            var updated = await decisionService.LinkArtifactAsync(decisionId, link, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        decisions.MapGet("/{decisionId:guid}/history", async (
            Guid decisionId,
            IDecisionService decisionService,
            CancellationToken ct) =>
        {
            var events = await decisionService.GetHistoryAsync(decisionId, ct);
            return Results.Ok(events);
        });

        // ── Financial Consequence Engine ──────────────────────────────
        decisions.MapPost("/{decisionId:guid}/financial-consequence", async (
            Guid decisionId,
            AttachFinancialConsequenceRequest req,
            IDecisionService decisionService,
            IFinancialConsequenceService finService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var decision = await decisionService.GetAsync(decisionId, ct);
            if (decision is null) return Results.NotFound();

            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";

            var consequence = new FinancialConsequence(
                Id: Guid.NewGuid(),
                DecisionId: decisionId,
                TenantId: decision.TenantId,
                ExpectedRevenueImpactLow: req.ExpectedRevenueImpactLow,
                ExpectedRevenueImpactHigh: req.ExpectedRevenueImpactHigh,
                ExpectedCostImpactLow: req.ExpectedCostImpactLow,
                ExpectedCostImpactHigh: req.ExpectedCostImpactHigh,
                ExpectedMarginImpact: req.ExpectedMarginImpact,
                ExpectedCashTimingImpact: req.ExpectedCashTimingImpact,
                LaborImpact: req.LaborImpact,
                DownsideRisk: req.DownsideRisk,
                UpsidePotential: req.UpsidePotential,
                ConfidenceAdjustment: req.ConfidenceAdjustment,
                RoiEstimateLow: req.RoiEstimateLow,
                RoiEstimateHigh: req.RoiEstimateHigh,
                BreakEvenEstimate: req.BreakEvenEstimate,
                Assumptions: req.Assumptions ?? Array.Empty<string>(),
                Notes: req.Notes,
                CreatedBy: userId,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                UpdatedAtUtc: DateTimeOffset.UtcNow);

            var created = await finService.AttachAsync(consequence, ct);
            return Results.Created($"/api/v1/decisions/{decisionId}/financial-consequence", created);
        });

        decisions.MapGet("/{decisionId:guid}/financial-consequence", async (
            Guid decisionId,
            IFinancialConsequenceService finService,
            CancellationToken ct) =>
        {
            var consequence = await finService.GetByDecisionAsync(decisionId, ct);
            return consequence is null ? Results.NotFound() : Results.Ok(consequence);
        });

        decisions.MapPut("/{decisionId:guid}/financial-consequence", async (
            Guid decisionId,
            AttachFinancialConsequenceRequest req,
            IFinancialConsequenceService finService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var existing = await finService.GetByDecisionAsync(decisionId, ct);
            if (existing is null) return Results.NotFound();

            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";

            var updated = existing with
            {
                ExpectedRevenueImpactLow = req.ExpectedRevenueImpactLow,
                ExpectedRevenueImpactHigh = req.ExpectedRevenueImpactHigh,
                ExpectedCostImpactLow = req.ExpectedCostImpactLow,
                ExpectedCostImpactHigh = req.ExpectedCostImpactHigh,
                ExpectedMarginImpact = req.ExpectedMarginImpact,
                ExpectedCashTimingImpact = req.ExpectedCashTimingImpact,
                LaborImpact = req.LaborImpact,
                DownsideRisk = req.DownsideRisk,
                UpsidePotential = req.UpsidePotential,
                ConfidenceAdjustment = req.ConfidenceAdjustment,
                RoiEstimateLow = req.RoiEstimateLow,
                RoiEstimateHigh = req.RoiEstimateHigh,
                BreakEvenEstimate = req.BreakEvenEstimate,
                Assumptions = req.Assumptions ?? existing.Assumptions,
                Notes = req.Notes ?? existing.Notes,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };

            var result = await finService.UpdateAsync(decisionId, updated, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
    }

    private static void MapOutcomesEndpoints(IEndpointRouteBuilder v1)
    {
        // ── Outcome Learning ──────────────────────────────────────
        var outcomes = v1.MapGroup("/outcomes")
            .WithTags("outcome-learning");

        outcomes.MapPost("/expected", async (
            RecordExpectedOutcomeRequest req,
            HttpContext ctx,
            IOutcomeLearningService outcomeSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            var userId = ctx.User?.FindFirst("sub")?.Value ?? "unknown";
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var record = await outcomeSvc.RecordExpectedOutcomeAsync(
                req.DecisionId, tenantId,
                req.ExpectedSummary, req.ExpectedValue,
                req.ConfidenceAtPrediction, req.ExpectedTimeframe,
                userId, ct);
            return Results.Created($"/api/v1/outcomes/{req.DecisionId}", record);
        }).RequireAuthorization("OperatorOrAdmin");

        outcomes.MapPost("/actual", async (
            RecordActualOutcomeRequest req,
            HttpContext ctx,
            IOutcomeLearningService outcomeSvc,
            CancellationToken ct) =>
        {
            var userId = ctx.User?.FindFirst("sub")?.Value ?? "unknown";
            try
            {
                var record = await outcomeSvc.RecordActualOutcomeAsync(
                    req.DecisionId, req.ActualSummary, req.ActualValue,
                    req.RootCause, req.Notes, userId, ct);
                return Results.Ok(record);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = "No expected outcome found for this decision." });
            }
        }).RequireAuthorization("OperatorOrAdmin");

        outcomes.MapGet("/{decisionId:guid}", async (
            Guid decisionId,
            IOutcomeLearningService outcomeSvc,
            CancellationToken ct) =>
        {
            var record = await outcomeSvc.GetOutcomeAsync(decisionId, ct);
            return record is null ? Results.NotFound() : Results.Ok(record);
        }).RequireAuthorization("GovernanceRead");

        outcomes.MapGet("/", async (
            int? limit,
            HttpContext ctx,
            IOutcomeLearningService outcomeSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var records = await outcomeSvc.ListOutcomesAsync(tenantId, limit ?? 50, ct);
            return Results.Ok(records);
        }).RequireAuthorization("GovernanceRead");

        outcomes.MapGet("/calibration", async (
            string? domain,
            HttpContext ctx,
            IOutcomeLearningService outcomeSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var summary = await outcomeSvc.GetCalibrationSummaryAsync(tenantId, domain, ct);
            return Results.Ok(summary);
        }).RequireAuthorization("GovernanceRead");
    }

    private static void MapProofAnalyticsEndpoints(IEndpointRouteBuilder v1)
    {
        // ── Proof Analytics ───────────────────────────────────────
        var proof = v1.MapGroup("/proof-analytics")
            .WithTags("proof-analytics");

        proof.MapPost("/events", async (
            RecordProofEventRequest req,
            HttpContext ctx,
            IProofAnalyticsService proofSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            var userId = ctx.User?.FindFirst("sub")?.Value ?? "unknown";
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            if (!Enum.TryParse<ProofEventType>(req.EventType, true, out var eventType))
                return Results.BadRequest(new { error = $"Invalid event type: {req.EventType}." });

            var proofEvent = new ProofEvent(
                Id: Guid.NewGuid(),
                TenantId: tenantId,
                DecisionId: req.DecisionId,
                WorkflowId: req.WorkflowId,
                EventType: eventType,
                Actor: userId,
                Detail: req.Detail,
                ExpectedValue: req.ExpectedValue,
                ActualValue: req.ActualValue,
                Variance: req.Variance,
                VariancePercent: req.VariancePercent,
                ActionType: req.ActionType,
                IsSuccess: req.IsSuccess,
                OverrideReason: req.OverrideReason,
                EconomicImpact: req.EconomicImpact,
                ImpactAttribution: req.ImpactAttribution,
                OccurredAtUtc: DateTimeOffset.UtcNow);

            var created = await proofSvc.RecordEventAsync(proofEvent, ct);
            return Results.Created($"/api/v1/proof-analytics/timeline/{req.DecisionId}", created);
        }).RequireAuthorization("OperatorOrAdmin");

        proof.MapGet("/timeline/{decisionId:guid}", async (
            Guid decisionId,
            IProofAnalyticsService proofSvc,
            CancellationToken ct) =>
        {
            var timeline = await proofSvc.GetTimelineAsync(decisionId, ct);
            return timeline is null ? Results.NotFound() : Results.Ok(timeline);
        }).RequireAuthorization("GovernanceRead");

        proof.MapGet("/workflow/{workflowId:guid}/timelines", async (
            Guid workflowId,
            IProofAnalyticsService proofSvc,
            CancellationToken ct) =>
        {
            var timelines = await proofSvc.GetWorkflowTimelinesAsync(workflowId, ct);
            return Results.Ok(timelines);
        }).RequireAuthorization("GovernanceRead");

        proof.MapGet("/predicted-vs-actual", async (
            string? domain, int? limit,
            HttpContext ctx,
            IProofAnalyticsService proofSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var summary = await proofSvc.GetPredictedVsActualAsync(tenantId, domain, limit ?? 50, ct);
            return Results.Ok(summary);
        }).RequireAuthorization("GovernanceRead");

        proof.MapGet("/approval-conversion", async (
            HttpContext ctx,
            IProofAnalyticsService proofSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var summary = await proofSvc.GetApprovalConversionAsync(tenantId, ct);
            return Results.Ok(summary);
        }).RequireAuthorization("GovernanceRead");

        proof.MapGet("/execution-trends", async (
            int? buckets,
            HttpContext ctx,
            IProofAnalyticsService proofSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var summary = await proofSvc.GetExecutionTrendsAsync(tenantId, buckets ?? 10, ct);
            return Results.Ok(summary);
        }).RequireAuthorization("GovernanceRead");

        proof.MapGet("/override-rates", async (
            HttpContext ctx,
            IProofAnalyticsService proofSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var summary = await proofSvc.GetOverrideRatesAsync(tenantId, ct);
            return Results.Ok(summary);
        }).RequireAuthorization("GovernanceRead");

        proof.MapGet("/trust-analytics", async (
            HttpContext ctx,
            IProofAnalyticsService proofSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var summary = await proofSvc.GetTrustAnalyticsAsync(tenantId, ct);
            return Results.Ok(summary);
        }).RequireAuthorization("GovernanceRead");

        proof.MapGet("/dashboard", async (
            string? domain,
            HttpContext ctx,
            IProofAnalyticsService proofSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var dashboard = await proofSvc.GetDashboardAsync(tenantId, domain, ct);
            return Results.Ok(dashboard);
        }).RequireAuthorization("GovernanceRead");
    }
}

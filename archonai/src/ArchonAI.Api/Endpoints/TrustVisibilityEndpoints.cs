using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.ActionSafety;
using ArchonAI.Core.Models.Governance;

namespace ArchonAI.Api.Endpoints;

/// <summary>
/// Composite trust-visibility endpoints that aggregate cross-domain lineage data
/// into single, operator/executive-friendly responses.
/// These endpoints read from existing services — they do not duplicate data.
/// </summary>
public static class TrustVisibilityEndpoints
{
    public static IEndpointRouteBuilder MapTrustVisibilityEndpoints(this IEndpointRouteBuilder v1)
    {
        var trust = v1.MapGroup("/trust-visibility")
            .WithTags("trust-visibility");

        // ── Trust Lineage: full decision→approval→execution→outcome trace ──

        trust.MapGet("/lineage/{decisionId:guid}", async (
            Guid decisionId,
            IDecisionService decisionSvc,
            IGovernanceService govSvc,
            IActionSafetyService safetySvc,
            IOutcomeLearningService outcomeSvc,
            IProofAnalyticsService proofSvc,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            // 1. Decision record
            var decision = await decisionSvc.GetAsync(decisionId, ct);
            if (decision is null) return Results.NotFound();

            // 2. Decision history (status transitions)
            var decisionHistory = await decisionSvc.GetHistoryAsync(decisionId, ct);

            // 3. Proof timeline (approval events, execution events, outcome events)
            var proofTimeline = await proofSvc.GetTimelineAsync(decisionId, ct);

            // 4. Outcome record (predicted vs actual)
            var outcome = await outcomeSvc.GetOutcomeAsync(decisionId, ct);

            // 5. Governed actions linked to this decision (with safety classification)
            var actions = await safetySvc.ListActionsAsync(tenantId, 100, ct);
            var linkedActions = actions
                .Where(a => a.DecisionId == decisionId)
                .Select(a => new
                {
                    a.Id,
                    a.ActionType,
                    a.Description,
                    a.Status,
                    a.ExecutedBy,
                    a.ExecutedAtUtc,
                    safety = new
                    {
                        a.SafetyClassification.Reversibility,
                        a.SafetyClassification.RollbackSupported,
                        a.SafetyClassification.RollbackStrategy,
                        rollbackWindow = a.SafetyClassification.RollbackWindow?.TotalMinutes,
                        a.SafetyClassification.SafetySummary,
                    },
                    rollbackAttempts = a.RollbackHistory.Count,
                    a.CompensationOutcome,
                    approvalGateId = a.ApprovalGateId,
                })
                .ToList();

            // 6. Resolve linked approval gates
            var approvalGates = new List<object>();
            foreach (var action in linkedActions.Where(a => a.approvalGateId.HasValue))
            {
                var gate = await govSvc.GetApprovalAsync(action.approvalGateId!.Value, tenantClaim, ct);
                if (gate is not null)
                {
                    approvalGates.Add(new
                    {
                        gate.Id,
                        gate.ActionType,
                        gate.ResourceId,
                        gate.RequestedBy,
                        gate.Justification,
                        status = gate.Status.ToString(),
                        gate.ReviewedBy,
                        gate.ReviewNotes,
                        gate.RequestedAtUtc,
                        gate.ReviewedAtUtc,
                        executionStatus = gate.ExecutionStatus.ToString(),
                        gate.ExecutionError,
                        gate.ExecutedAtUtc,
                    });
                }
            }

            // Compose lineage response
            var lineage = new
            {
                decisionId,
                decision = new
                {
                    decision.Title,
                    decision.Domain,
                    decision.Objective,
                    reversibility = decision.Reversibility.ToString(),
                    riskLevel = decision.RiskLevel.ToString(),
                    decision.Confidence,
                    decision.ExpectedValue,
                    status = decision.Status.ToString(),
                    decision.CreatedBy,
                    decision.CreatedAtUtc,
                    decision.RequiresApproval,
                },
                approvalGates,
                governedActions = linkedActions,
                outcome = outcome is null ? null : new
                {
                    outcome.ExpectedOutcomeSummary,
                    outcome.ExpectedValue,
                    outcome.ConfidenceAtPrediction,
                    outcome.ActualOutcomeSummary,
                    outcome.ActualValue,
                    outcome.ValueVariance,
                    outcome.VariancePercent,
                    direction = outcome.Direction.ToString(),
                    assessment = outcome.Assessment.ToString(),
                    outcome.RecalibrationSignal,
                    outcome.CreatedAtUtc,
                    outcome.UpdatedAtUtc,
                },
                proofTimeline = proofTimeline is null ? null : new
                {
                    proofTimeline.DecisionTitle,
                    proofTimeline.Domain,
                    totalEvents = proofTimeline.Events.Count,
                    events = proofTimeline.Events.Select(e => new
                    {
                        e.EventType,
                        e.Actor,
                        e.Detail,
                        e.ExpectedValue,
                        e.ActualValue,
                        e.IsSuccess,
                        e.OverrideReason,
                        e.EconomicImpact,
                        e.OccurredAtUtc,
                    }),
                    proofTimeline.Summary,
                },
                statusHistory = decisionHistory,
                lineageSummary = new
                {
                    hasApproval = approvalGates.Count > 0,
                    hasExecution = linkedActions.Count > 0,
                    hasOutcome = outcome is not null,
                    hasProofTrail = proofTimeline is not null,
                    allActionsReversible = linkedActions.All(a =>
                        a.safety.Reversibility == ReversibilityLevel.Reversible),
                    anyRollbackAttempted = linkedActions.Any(a => a.rollbackAttempts > 0),
                    varianceWithinThreshold = outcome is null || outcome.VariancePercent is null
                        || Math.Abs(outcome.VariancePercent!.Value) <= 20.0,
                },
            };

            return Results.Ok(lineage);
        }).RequireAuthorization("GovernanceRead");

        // ── Trust Posture: tenant-wide governance health summary ──

        trust.MapGet("/posture", async (
            HttpContext ctx,
            IGovernanceService govSvc,
            IActionSafetyService safetySvc,
            ITrustTierService trustSvc,
            IOutcomeLearningService outcomeSvc,
            IProofAnalyticsService proofSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            // Governance: approval policies and pending queue
            var policies = await govSvc.ListApprovalPoliciesAsync(ct);
            var pending = await govSvc.ListPendingApprovalsAsync(tenantClaim, ct);
            var history = await govSvc.GetApprovalHistoryAsync(tenantClaim, null, 200, ct);

            int approved = history.Count(h => h.Outcome == ApprovalStatus.Approved);
            int denied = history.Count(h => h.Outcome == ApprovalStatus.Denied);

            // Safety: rollback summary
            var rollbackSummary = await safetySvc.GetRollbackSummaryAsync(tenantId, ct);

            // Trust tiers: policy coverage
            var tierPolicies = await trustSvc.ListPoliciesAsync(tenantClaim, ct);

            // Outcomes: calibration
            var calibration = await outcomeSvc.GetCalibrationSummaryAsync(tenantId, null, ct);

            // Proof analytics: dashboard
            var proofDashboard = await proofSvc.GetDashboardAsync(tenantId, null, ct);

            var posture = new
            {
                tenantId,
                generatedAtUtc = DateTimeOffset.UtcNow,
                governance = new
                {
                    activePolicies = policies.Count(p => p.IsEnabled),
                    totalPolicies = policies.Count,
                    pendingApprovals = pending.Count,
                    recentApprovals = new
                    {
                        total = history.Count,
                        approved,
                        denied,
                        approvalRate = history.Count > 0
                            ? Math.Round((double)approved / history.Count, 3)
                            : 0.0,
                    },
                    separationOfDutiesEnforced = policies.Any(p => p.RequireSeparationOfDuties),
                },
                safety = new
                {
                    rollbackSummary.TotalActions,
                    rollbackSummary.Reversible,
                    rollbackSummary.Compensatable,
                    rollbackSummary.Irreversible,
                    reversibilityRate = rollbackSummary.TotalActions > 0
                        ? Math.Round((double)(rollbackSummary.Reversible + rollbackSummary.Compensatable) / rollbackSummary.TotalActions, 3)
                        : 1.0,
                    rollbackSummary.RollbacksAttempted,
                    rollbackSummary.RollbacksSucceeded,
                    rollbackSummary.RollbacksFailed,
                    rollbackSuccessRate = rollbackSummary.RollbacksAttempted > 0
                        ? Math.Round((double)rollbackSummary.RollbacksSucceeded / rollbackSummary.RollbacksAttempted, 3)
                        : 1.0,
                    rollbackSummary.WithinRollbackWindow,
                    rollbackSummary.WindowExpired,
                },
                trustTiers = new
                {
                    totalPolicies = tierPolicies.Count,
                    enabledPolicies = tierPolicies.Count(p => p.IsEnabled),
                    actionsCovered = tierPolicies.Select(p => p.ActionScope).Distinct().Count(),
                    requiresReversible = tierPolicies.Count(p => p.RequireReversible),
                },
                outcomes = calibration,
                proofAnalytics = proofDashboard,
            };

            return Results.Ok(posture);
        }).RequireAuthorization("GovernanceRead");

        return v1;
    }
}

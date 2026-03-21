using System.Text.Json;
using ArchonAI.Api.Dtos;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.ExceptionIntelligence;
using ArchonAI.Core.Models.ExecutiveCommand;
using ArchonAI.Core.Models.HeroWorkflow;
using ArchonAI.Core.Models.HumanOverride;
using ArchonAI.Core.Models.OperationalTwin;
using ArchonAI.Core.Models.PolicySimulation;
using ArchonAI.Core.Models.Scenario;

namespace ArchonAI.Api.Endpoints;

public static class OperatorEndpoints
{
    public static IEndpointRouteBuilder MapOperatorEndpoints(this IEndpointRouteBuilder v1)
    {
        MapOverridesEndpoints(v1);
        MapTwinEndpoints(v1);
        MapScenariosEndpoints(v1);
        MapExceptionsEndpoints(v1);
        MapExecutiveCommandEndpoints(v1);
        MapHeroWorkflowEndpoints(v1);
        MapPolicySimulationEndpoints(v1);
        MapInspectionEndpoints(v1);
        return v1;
    }

    private static void MapOverridesEndpoints(IEndpointRouteBuilder v1)
    {
        var overrides = v1.MapGroup("/overrides")
            .RequireAuthorization("OperatorOrAdmin");

        overrides.MapPost("/pause", async (
            PauseWorkflowRequest req,
            IHumanOverrideService overrideSvc,
            CancellationToken ct) =>
        {
            var result = await overrideSvc.PauseWorkflowAsync(req, ct);
            return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
        });

        overrides.MapPost("/resume", async (
            ResumeWorkflowRequest req,
            IHumanOverrideService overrideSvc,
            CancellationToken ct) =>
        {
            var result = await overrideSvc.ResumeWorkflowAsync(req, ct);
            return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
        });

        overrides.MapPost("/cancel", async (
            CancelActionRequest req,
            IHumanOverrideService overrideSvc,
            CancellationToken ct) =>
        {
            var result = await overrideSvc.CancelActionAsync(req, ct);
            return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
        });

        overrides.MapPost("/modify-strategy", async (
            ModifyStrategyRequest req,
            HttpContext ctx,
            IHumanOverrideService overrideSvc,
            IGovernanceService gov,
            CancellationToken ct) =>
        {
            var tenantId = ctx.User?.FindFirst("tenant_id")?.Value;
            var userId = ctx.User?.FindFirst("sub")?.Value
                ?? ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (tenantId is null || userId is null) return Results.Unauthorized();

            if (await gov.RequiresApprovalAsync("strategy.override", ct))
            {
                var payload = JsonSerializer.Serialize(req);
                var gate = await gov.RequestApprovalAsync(
                    "strategy.override", req.WorkflowId.ToString(), tenantId, userId,
                    "Strategy override requested", payload, ct);
                return Results.Accepted($"/api/v1/governance/{gate.Id}", gate);
            }

            var result = await overrideSvc.ModifyStrategyAsync(req, ct);
            return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
        });

        overrides.MapPost("/rollback", async (
            RollbackRequest req,
            IHumanOverrideService overrideSvc,
            CancellationToken ct) =>
        {
            var result = await overrideSvc.RollbackAsync(req, ct);
            return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
        });

        overrides.MapGet("/log", async (
            Guid? workflowId,
            int? limit,
            IHumanOverrideService overrideSvc,
            CancellationToken ct) =>
        {
            var log = await overrideSvc.GetOverrideLogAsync(workflowId, limit ?? 100, ct);
            return Results.Ok(log);
        });
    }

    private static void MapTwinEndpoints(IEndpointRouteBuilder v1)
    {
        var twin = v1.MapGroup("/twin")
            .WithTags("operational-twin");

        twin.MapPost("/entities", async (
            UpsertTwinEntityRequest req,
            HttpContext ctx,
            IOperationalTwinService twinSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            var userId = ctx.User?.FindFirst("sub")?.Value ?? "unknown";
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });
            if (!Enum.TryParse<TwinEntityType>(req.EntityType, true, out var entityType))
                return Results.BadRequest(new { error = $"Invalid entity type: {req.EntityType}." });

            var now = DateTimeOffset.UtcNow;
            var entity = new TwinEntity(
                Id: req.Id ?? Guid.NewGuid(), TenantId: tenantId, EntityType: entityType,
                Name: req.Name, Description: req.Description,
                Status: Enum.TryParse<TwinEntityStatus>(req.Status, true, out var s) ? s : TwinEntityStatus.Active,
                Properties: req.Properties?.AsReadOnly() ?? new Dictionary<string, string>().AsReadOnly(),
                Tags: req.Tags ?? Array.Empty<string>(),
                CreatedBy: userId, CreatedAtUtc: now, UpdatedAtUtc: now);

            var created = await twinSvc.UpsertEntityAsync(entity, ct);
            return Results.Created($"/api/v1/twin/entities/{created.Id}", created);
        }).RequireAuthorization("OperatorOrAdmin");

        twin.MapGet("/entities", async (
            string? type,
            HttpContext ctx,
            IOperationalTwinService twinSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            TwinEntityType? parsedType = null;
            if (type is not null && Enum.TryParse<TwinEntityType>(type, true, out var t))
                parsedType = t;

            var entities = await twinSvc.ListEntitiesAsync(tenantId, parsedType, ct);
            return Results.Ok(entities);
        }).RequireAuthorization("GovernanceRead");

        twin.MapGet("/entities/{entityId:guid}", async (
            Guid entityId,
            HttpContext ctx,
            IOperationalTwinService twinSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var entity = await twinSvc.GetEntityAsync(entityId, tenantId, ct);
            return entity is null ? Results.NotFound() : Results.Ok(entity);
        }).RequireAuthorization("GovernanceRead");

        twin.MapPost("/dependencies", async (
            AddTwinDependencyRequest req,
            HttpContext ctx,
            IOperationalTwinService twinSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });
            if (!Enum.TryParse<DependencyType>(req.Type, true, out var depType))
                return Results.BadRequest(new { error = $"Invalid dependency type: {req.Type}." });

            var dep = new TwinDependency(Guid.NewGuid(), tenantId,
                req.FromEntityId, req.ToEntityId, depType,
                req.Label, req.CriticalityScore, DateTimeOffset.UtcNow);
            var created = await twinSvc.AddDependencyAsync(dep, ct);
            return Results.Created($"/api/v1/twin/dependencies/{created.Id}", created);
        }).RequireAuthorization("OperatorOrAdmin");

        twin.MapGet("/entities/{entityId:guid}/dependencies", async (
            Guid entityId,
            HttpContext ctx,
            IOperationalTwinService twinSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var deps = await twinSvc.GetDependenciesAsync(entityId, tenantId, ct);
            return Results.Ok(deps);
        }).RequireAuthorization("GovernanceRead");

        twin.MapPost("/kpis", async (
            RecordTwinKpiRequest req,
            IOperationalTwinService twinSvc,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<KpiDirection>(req.Direction, true, out var dir))
                dir = KpiDirection.HigherIsBetter;

            var kpi = new TwinKpi(req.EntityId, req.MetricName, req.CurrentValue,
                req.TargetValue, req.ThresholdWarning, req.ThresholdCritical,
                dir, req.Unit ?? "", DateTimeOffset.UtcNow);
            var created = await twinSvc.RecordKpiAsync(kpi, ct);
            return Results.Ok(created);
        }).RequireAuthorization("OperatorOrAdmin");

        twin.MapGet("/entities/{entityId:guid}/kpis", async (
            Guid entityId,
            IOperationalTwinService twinSvc,
            CancellationToken ct) =>
        {
            var kpis = await twinSvc.GetKpisAsync(entityId, ct);
            return Results.Ok(kpis);
        }).RequireAuthorization("GovernanceRead");

        twin.MapPost("/bottlenecks", async (
            ReportBottleneckRequest req,
            HttpContext ctx,
            IOperationalTwinService twinSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });
            if (!Enum.TryParse<BottleneckSeverity>(req.Severity, true, out var sev))
                sev = BottleneckSeverity.Medium;

            var bn = new TwinBottleneck(Guid.NewGuid(), tenantId, req.AffectedEntityId,
                req.Description, sev, req.RootCause, false, DateTimeOffset.UtcNow, null);
            var created = await twinSvc.ReportBottleneckAsync(bn, ct);
            return Results.Created($"/api/v1/twin/bottlenecks/{created.Id}", created);
        }).RequireAuthorization("OperatorOrAdmin");

        twin.MapGet("/bottlenecks", async (
            bool? activeOnly,
            HttpContext ctx,
            IOperationalTwinService twinSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var bns = await twinSvc.ListBottlenecksAsync(tenantId, activeOnly ?? true, ct);
            return Results.Ok(bns);
        }).RequireAuthorization("GovernanceRead");

        twin.MapPost("/bottlenecks/{bottleneckId:guid}/resolve", async (
            Guid bottleneckId,
            HttpContext ctx,
            IOperationalTwinService twinSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var resolved = await twinSvc.ResolveBottleneckAsync(bottleneckId, tenantId, ct);
            return resolved is null ? Results.NotFound() : Results.Ok(resolved);
        }).RequireAuthorization("OperatorOrAdmin");

        twin.MapPost("/entities/{entityId:guid}/links", async (
            Guid entityId,
            LinkTwinArtifactRequest req,
            IOperationalTwinService twinSvc,
            CancellationToken ct) =>
        {
            var link = new TwinArtifactLink(Guid.NewGuid(), entityId,
                req.ArtifactType, req.ArtifactId, req.Relationship, DateTimeOffset.UtcNow);
            var created = await twinSvc.LinkArtifactAsync(link, ct);
            return Results.Ok(created);
        }).RequireAuthorization("OperatorOrAdmin");

        twin.MapGet("/entities/{entityId:guid}/links", async (
            Guid entityId,
            IOperationalTwinService twinSvc,
            CancellationToken ct) =>
        {
            var links = await twinSvc.GetArtifactLinksAsync(entityId, ct);
            return Results.Ok(links);
        }).RequireAuthorization("GovernanceRead");

        twin.MapGet("/overview", async (
            HttpContext ctx,
            IOperationalTwinService twinSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var overview = await twinSvc.GetOverviewAsync(tenantId, ct);
            return Results.Ok(overview);
        }).RequireAuthorization("GovernanceRead");
    }

    private static void MapScenariosEndpoints(IEndpointRouteBuilder v1)
    {
        var scenarios = v1.MapGroup("/scenarios")
            .WithTags("scenarios")
            .RequireRateLimiting("api");

        scenarios.MapPost("/", async (
            CreateScenarioRequest req,
            HttpContext ctx,
            IScenarioService scenarioSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            if (!Enum.TryParse<ScenarioType>(req.Type, true, out var scenarioType))
                return Results.BadRequest(new { error = $"Invalid scenario type: {req.Type}" });

            var assumptions = req.Assumptions?.Select(a =>
                new ScenarioAssumption(a.Name, a.CurrentValue, a.ProposedValue, a.Unit, a.Rationale)).ToList()
                ?? new List<ScenarioAssumption>();

            var linkedKpis = req.LinkedKpiIds?.Select(id =>
                new ScenarioLink("Kpi", id, null)).ToList()
                ?? new List<ScenarioLink>();

            var linkedDecisions = req.LinkedDecisionIds?.Select(id =>
                new ScenarioLink("Decision", id, null)).ToList()
                ?? new List<ScenarioLink>();

            var linkedEntities = req.LinkedEntityIds?.Select(id =>
                new ScenarioLink("TwinEntity", id, null)).ToList()
                ?? new List<ScenarioLink>();

            var now = DateTimeOffset.UtcNow;
            var user = ctx.User?.Identity?.Name ?? "system";

            var scenario = new Scenario(
                Id: Guid.NewGuid(),
                TenantId: tenantId,
                Title: req.Title,
                Description: req.Description,
                Type: scenarioType,
                Status: ScenarioStatus.Draft,
                Assumptions: assumptions,
                ProjectedEffects: Array.Empty<ProjectedEffect>(),
                LinkedKpis: linkedKpis,
                LinkedDecisions: linkedDecisions,
                LinkedEntities: linkedEntities,
                CreatedBy: user,
                CreatedAtUtc: now,
                UpdatedAtUtc: now);

            // Derive initial projected effects from assumptions
            if (assumptions.Count > 0)
            {
                var effects = ArchonAI.Api.Security.ScenarioService.DeriveProjectedEffects(scenario, assumptions);
                scenario = scenario with { ProjectedEffects = effects, Status = ScenarioStatus.Active };
            }

            var result = await scenarioSvc.CreateScenarioAsync(scenario, ct);
            return Results.Created($"/api/v1/scenarios/{result.Id}", result);
        }).RequireAuthorization("GovernanceWrite");

        scenarios.MapPut("/{scenarioId:guid}/assumptions", async (
            Guid scenarioId,
            UpdateAssumptionsRequest req,
            HttpContext ctx,
            IScenarioService scenarioSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var assumptions = req.Assumptions.Select(a =>
                new ScenarioAssumption(a.Name, a.CurrentValue, a.ProposedValue, a.Unit, a.Rationale)).ToList();

            var updated = await scenarioSvc.UpdateAssumptionsAsync(scenarioId, tenantId, assumptions, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }).RequireAuthorization("GovernanceWrite");

        scenarios.MapGet("/{scenarioId:guid}", async (
            Guid scenarioId,
            HttpContext ctx,
            IScenarioService scenarioSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var scenario = await scenarioSvc.GetScenarioAsync(scenarioId, tenantId, ct);
            return scenario is null ? Results.NotFound() : Results.Ok(scenario);
        }).RequireAuthorization("GovernanceRead");

        scenarios.MapGet("/", async (
            HttpContext ctx,
            IScenarioService scenarioSvc,
            string? type,
            string? status,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            ScenarioType? typeFilter = null;
            if (type is not null && Enum.TryParse<ScenarioType>(type, true, out var st))
                typeFilter = st;

            ScenarioStatus? statusFilter = null;
            if (status is not null && Enum.TryParse<ScenarioStatus>(status, true, out var ss))
                statusFilter = ss;

            var list = await scenarioSvc.ListScenariosAsync(tenantId, typeFilter, statusFilter, ct);
            return Results.Ok(list);
        }).RequireAuthorization("GovernanceRead");

        scenarios.MapPost("/compare", async (
            CompareScenarioRequest req,
            HttpContext ctx,
            IScenarioService scenarioSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            if (req.ScenarioIds.Count < 2)
                return Results.BadRequest(new { error = "At least 2 scenario IDs required for comparison." });

            var comparison = await scenarioSvc.CompareScenariosAsync(req.ScenarioIds, tenantId, ct);
            return Results.Ok(comparison);
        }).RequireAuthorization("GovernanceRead");

        scenarios.MapDelete("/{scenarioId:guid}", async (
            Guid scenarioId,
            HttpContext ctx,
            IScenarioService scenarioSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var deleted = await scenarioSvc.DeleteScenarioAsync(scenarioId, tenantId, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).RequireAuthorization("GovernanceWrite");
    }

    private static void MapExceptionsEndpoints(IEndpointRouteBuilder v1)
    {
        var exceptions = v1.MapGroup("/exceptions")
            .WithTags("exceptions")
            .RequireRateLimiting("api");

        exceptions.MapPost("/", async (
            RaiseExceptionRequest req,
            HttpContext ctx,
            IExceptionIntelligenceService exSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            if (!Enum.TryParse<ExceptionCategory>(req.Category, true, out var category))
                return Results.BadRequest(new { error = $"Invalid category: {req.Category}" });
            if (!Enum.TryParse<ExceptionSeverity>(req.Severity, true, out var severity))
                return Results.BadRequest(new { error = $"Invalid severity: {req.Severity}" });

            var escalation = EscalationLevel.None;
            if (req.EscalationLevel is not null)
                Enum.TryParse(req.EscalationLevel, true, out escalation);

            var links = req.LinkedArtifacts?.Select(l =>
                new ExceptionArtifactLink(l.ArtifactType, l.ArtifactId, l.Label)).ToList()
                ?? new List<ExceptionArtifactLink>();

            RecommendedAction? action = null;
            if (req.RecommendedAction is not null)
            {
                var ra = req.RecommendedAction;
                action = new RecommendedAction(ra.ActionType, ra.Description, ra.TargetArtifactType, ra.TargetArtifactId, ra.Confidence ?? "Medium");
            }

            var now = DateTimeOffset.UtcNow;
            var user = ctx.User?.Identity?.Name ?? "system";

            var exception = new OperationalException(
                Id: Guid.NewGuid(),
                TenantId: tenantId,
                Category: category,
                Severity: severity,
                Title: req.Title,
                Description: req.Description ?? "",
                Domain: req.Domain ?? "General",
                Status: ExceptionStatus.Open,
                Urgency: req.Urgency ?? 0.5,
                EconomicImpactEstimate: req.EconomicImpactEstimate ?? 0,
                Confidence: req.Confidence ?? 0.5,
                EscalationLevel: escalation,
                AssignedTo: req.AssignedTo,
                EscalationPath: req.EscalationPath,
                LinkedArtifacts: links,
                RecommendedAction: action,
                CreatedBy: user,
                CreatedAtUtc: now,
                UpdatedAtUtc: now,
                AcknowledgedAtUtc: null,
                ResolvedAtUtc: null);

            var result = await exSvc.RaiseExceptionAsync(exception, ct);
            return Results.Created($"/api/v1/exceptions/{result.Id}", result);
        }).RequireAuthorization("GovernanceWrite");

        exceptions.MapGet("/", async (
            HttpContext ctx,
            IExceptionIntelligenceService exSvc,
            string? severity, string? category, string? status, string? domain,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            ExceptionSeverity? sevFilter = null;
            if (severity is not null && Enum.TryParse<ExceptionSeverity>(severity, true, out var sv)) sevFilter = sv;
            ExceptionCategory? catFilter = null;
            if (category is not null && Enum.TryParse<ExceptionCategory>(category, true, out var cv)) catFilter = cv;
            ExceptionStatus? statusFilter = null;
            if (status is not null && Enum.TryParse<ExceptionStatus>(status, true, out var stv)) statusFilter = stv;

            var list = await exSvc.ListExceptionsAsync(tenantId, sevFilter, catFilter, statusFilter, domain, ct);
            return Results.Ok(list);
        }).RequireAuthorization("GovernanceRead");

        exceptions.MapGet("/{exceptionId:guid}", async (
            Guid exceptionId,
            HttpContext ctx,
            IExceptionIntelligenceService exSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var ex = await exSvc.GetExceptionAsync(exceptionId, tenantId, ct);
            return ex is null ? Results.NotFound() : Results.Ok(ex);
        }).RequireAuthorization("GovernanceRead");

        exceptions.MapPut("/{exceptionId:guid}/status", async (
            Guid exceptionId,
            UpdateExceptionStatusRequest req,
            HttpContext ctx,
            IExceptionIntelligenceService exSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            if (!Enum.TryParse<ExceptionStatus>(req.Status, true, out var status))
                return Results.BadRequest(new { error = $"Invalid status: {req.Status}" });

            var updated = await exSvc.UpdateStatusAsync(exceptionId, tenantId, status, req.AssignedTo, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }).RequireAuthorization("GovernanceWrite");

        exceptions.MapPost("/{exceptionId:guid}/recommended-action", async (
            Guid exceptionId,
            SetRecommendedActionRequest req,
            HttpContext ctx,
            IExceptionIntelligenceService exSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var action = new RecommendedAction(
                req.ActionType, req.Description,
                req.TargetArtifactType, req.TargetArtifactId,
                req.Confidence ?? "Medium");

            var updated = await exSvc.SetRecommendedActionAsync(exceptionId, tenantId, action, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }).RequireAuthorization("GovernanceWrite");

        exceptions.MapGet("/summary", async (
            HttpContext ctx,
            IExceptionIntelligenceService exSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var summary = await exSvc.GetQueueSummaryAsync(tenantId, ct);
            return Results.Ok(summary);
        }).RequireAuthorization("GovernanceRead");

        exceptions.MapGet("/prioritized", async (
            HttpContext ctx,
            IExceptionIntelligenceService exSvc,
            int? limit,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var queue = await exSvc.GetPrioritizedQueueAsync(tenantId, limit ?? 20, ct);
            return Results.Ok(queue);
        }).RequireAuthorization("GovernanceRead");
    }

    private static void MapExecutiveCommandEndpoints(IEndpointRouteBuilder v1)
    {
        var execCmd = v1.MapGroup("/executive-command")
            .WithTags("executive-command")
            .RequireRateLimiting("api");

        execCmd.MapGet("/summary", async (
            HttpContext ctx,
            IExecutiveCommandService cmdSvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var summary = await cmdSvc.GetCommandSummaryAsync(tenantId, ct);
            return Results.Ok(summary);
        }).RequireAuthorization("GovernanceRead");

        // Industry-specific KPIs — returns seed data for the Production Intelligence dashboard.
        // In production, these values would be aggregated from MES, ERP, QMS, and CMMS integrations.
        execCmd.MapGet("/industry-kpis", (string? industry) =>
        {
            if (string.Equals(industry, "healthcare", StringComparison.OrdinalIgnoreCase))
            {
                var healthcareKpis = new
                {
                    Industry = "healthcare",
                    GeneratedAtUtc = DateTimeOffset.UtcNow,
                    Kpis = new object[]
                    {
                        new {
                            Id = "days-in-ar",
                            Label = "Days in A/R",
                            Value = 38.2,
                            Unit = "days",
                            Trend = "down",
                            TrendDelta = -3.1,
                            Description = "Average days in accounts receivable across all payers"
                        },
                        new {
                            Id = "clean-claim-rate",
                            Label = "Clean Claim Rate",
                            Value = 94.7,
                            Unit = "percent",
                            Trend = "up",
                            TrendDelta = 2.3,
                            Description = "Percentage of claims accepted on first submission without errors"
                        },
                        new {
                            Id = "patient-throughput",
                            Label = "Patient Throughput",
                            Value = 3.8,
                            Unit = "patients/bed/day",
                            Trend = "up",
                            TrendDelta = 0.4,
                            Description = "Average patients processed per bed per day across all departments"
                        },
                        new {
                            Id = "auth-turnaround",
                            Label = "Authorization Turnaround",
                            Value = 18.5,
                            Unit = "hours",
                            Trend = "down",
                            TrendDelta = -6.2,
                            PriorPeriodValue = 24.7,
                            Description = "Average hours from authorization submission to payer response"
                        },
                        new {
                            Id = "denial-rate",
                            Label = "Denial Rate",
                            Value = 4.2,
                            Unit = "percent",
                            Trend = "down",
                            TrendDelta = -2.8,
                            Description = "Percentage of claims denied, with AI-driven reduction from coding error detection"
                        },
                        new {
                            Id = "no-show-rate",
                            Label = "No-Show Rate",
                            Value = 6.1,
                            Unit = "percent",
                            Trend = "down",
                            TrendDelta = -3.4,
                            Description = "Appointment no-show percentage with AI-driven reminder intervention impact"
                        },
                    }
                };
                return Results.Ok<object?>(healthcareKpis);
            }

            if (string.Equals(industry, "financial-services", StringComparison.OrdinalIgnoreCase))
            {
                var financialKpis = new
                {
                    Industry = "financial-services",
                    GeneratedAtUtc = DateTimeOffset.UtcNow,
                    Kpis = new object[]
                    {
                        new {
                            Id = "reg-filing-ontime",
                            Label = "Regulatory Filing On-Time Rate",
                            Value = 97.8,
                            Unit = "percent",
                            Trend = "up",
                            TrendDelta = 1.2,
                            Description = "Percentage of regulatory filings submitted before deadline"
                        },
                        new {
                            Id = "surveillance-alerts",
                            Label = "Surveillance Alert Volume",
                            Value = 142,
                            Unit = "count",
                            Trend = "down",
                            TrendDelta = -18,
                            Description = "Transaction surveillance alerts this period; 72% false positive rate (down from 84%)"
                        },
                        new {
                            Id = "rec-break-aging",
                            Label = "Reconciliation Break Aging",
                            Value = 23,
                            Unit = "count",
                            Description = "Open reconciliation breaks: 15 same-day, 6 at 1-3 days, 2 at 3+ days"
                        },
                        new {
                            Id = "onboarding-cycle",
                            Label = "Client Onboarding Cycle Time",
                            Value = 14.2,
                            Unit = "days",
                            Trend = "down",
                            TrendDelta = -8.3,
                            PriorPeriodValue = 22.5,
                            Description = "Average days from initial client contact to account activation"
                        },
                        new {
                            Id = "oprisk-events",
                            Label = "Operational Risk Events",
                            Value = 7,
                            Unit = "count",
                            Severity = "medium",
                            Description = "Operational risk events this period: 1 high, 4 medium, 2 low severity"
                        },
                        new {
                            Id = "aum-monitored",
                            Label = "AUM Under Active Monitoring",
                            Value = 2400000000.0,
                            Unit = "dollars",
                            Trend = "up",
                            Description = "Total assets under management with active portfolio drift monitoring"
                        },
                    }
                };
                return Results.Ok<object?>(financialKpis);
            }

            if (string.Equals(industry, "energy", StringComparison.OrdinalIgnoreCase))
            {
                var energyKpis = new
                {
                    Industry = "energy",
                    GeneratedAtUtc = DateTimeOffset.UtcNow,
                    Kpis = new object[]
                    {
                        new {
                            Id = "equipment-availability",
                            Label = "Equipment Availability Rate",
                            Value = 96.4,
                            Unit = "percent",
                            Trend = "up",
                            TrendDelta = 1.1,
                            Description = "Percentage of generation and T&D equipment available for service"
                        },
                        new {
                            Id = "mtbf",
                            Label = "MTBF",
                            Value = 4320,
                            Unit = "hours",
                            Trend = "up",
                            TrendDelta = 280,
                            PriorPeriodValue = 4040,
                            Description = "Mean time between failures across monitored asset fleet"
                        },
                        new {
                            Id = "unplanned-outage-min",
                            Label = "Unplanned Outage Minutes",
                            Value = 847,
                            Unit = "minutes",
                            Trend = "down",
                            TrendDelta = -192,
                            PriorPeriodValue = 1039,
                            Description = "Total unplanned outage minutes this period across all feeders"
                        },
                        new {
                            Id = "safety-incident-rate",
                            Label = "Safety Incident Rate",
                            Value = 0.42,
                            Unit = "rate",
                            Trend = "down",
                            TrendDelta = -0.11,
                            Description = "OSHA recordable incident rate per 200,000 hours worked"
                        },
                        new {
                            Id = "reg-compliance-score",
                            Label = "Regulatory Compliance Score",
                            Value = 98.1,
                            Unit = "percent",
                            Trend = "up",
                            TrendDelta = 0.8,
                            Description = "Composite compliance score across NERC CIP, EPA, and FERC standards"
                        },
                        new {
                            Id = "renewable-ratio",
                            Label = "Renewable Generation Ratio",
                            Value = 34.7,
                            Unit = "percent",
                            Trend = "up",
                            TrendDelta = 3.2,
                            Description = "Percentage of total generation from renewable sources"
                        },
                    }
                };
                return Results.Ok<object?>(energyKpis);
            }

            if (!string.Equals(industry, "manufacturing", StringComparison.OrdinalIgnoreCase))
                return Results.Ok<object?>(null);

            // Seed data — replace with real MES/ERP aggregation queries per tenant
            var kpis = new
            {
                Industry = "manufacturing",
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                Kpis = new object[]
                {
                    new {
                        Id = "oee-impact",
                        Label = "OEE Impact",
                        Value = 87.3,
                        Unit = "percent",
                        Trend = "up",          // "up" | "down" | "flat"
                        TrendDelta = 2.1,       // percentage points vs. prior period
                        Description = "Overall Equipment Effectiveness improvement attributed to ArchonAI coordination"
                        // Real implementation: aggregate availability × performance × quality from MES telemetry
                    },
                    new {
                        Id = "unplanned-downtime",
                        Label = "Unplanned Downtime Hours",
                        Value = 12.5,
                        Unit = "hours",
                        Trend = "down",
                        TrendDelta = -4.2,
                        PriorPeriodValue = 16.7,
                        Description = "Total unplanned downtime hours this period vs. prior period"
                        // Real implementation: sum downtime events from CMMS/MES filtered by 'unplanned' reason code
                    },
                    new {
                        Id = "quality-escapes",
                        Label = "Quality Escape Count",
                        Value = 2,
                        Unit = "count",
                        Severity = "medium",    // "low" | "medium" | "high" | "critical"
                        Description = "Number of quality escapes (defective product reaching downstream) this period"
                        // Real implementation: count QMS non-conformance records where escape=true
                    },
                    new {
                        Id = "schedule-adherence",
                        Label = "Schedule Adherence",
                        Value = 94.1,
                        Unit = "percent",
                        Trend = "up",
                        TrendDelta = 1.8,
                        Description = "Percentage of production orders completed on or before scheduled date"
                        // Real implementation: (on-time completed orders / total scheduled orders) × 100
                    },
                    new {
                        Id = "mtter",
                        Label = "Mean Time to Exception Resolution",
                        Value = 2.3,
                        Unit = "hours",
                        Trend = "down",
                        TrendDelta = -0.8,
                        Description = "Average time from exception detection to resolution"
                        // Real implementation: avg(resolved_at - detected_at) from exception_intelligence table
                    },
                    new {
                        Id = "supplier-exceptions",
                        Label = "Open Supplier Exceptions",
                        Value = 5,
                        Unit = "count",
                        EconomicExposure = 142000.0,
                        Description = "Active supplier-related exceptions with estimated financial exposure"
                        // Real implementation: count open exceptions where category='supplier', sum economic_impact_estimate
                    },
                }
            };

            return Results.Ok<object?>(kpis);
        }).RequireAuthorization("GovernanceRead");
    }

    private static void MapHeroWorkflowEndpoints(IEndpointRouteBuilder v1)
    {
        var heroWorkflows = v1.MapGroup("/hero-workflows")
            .WithTags("hero-workflows")
            .RequireRateLimiting("api");

        heroWorkflows.MapGet("/catalog", async (
            IHeroWorkflowService heroSvc,
            CancellationToken ct) =>
        {
            var catalog = await heroSvc.GetCatalogAsync(ct);
            return Results.Ok(catalog);
        }).RequireAuthorization("GovernanceRead");

        heroWorkflows.MapGet("/catalog/{workflowType}", async (
            string workflowType,
            IHeroWorkflowService heroSvc,
            CancellationToken ct) =>
        {
            var def = await heroSvc.GetDefinitionAsync(workflowType, ct);
            return def is null ? Results.NotFound() : Results.Ok(def);
        }).RequireAuthorization("GovernanceRead");

        heroWorkflows.MapPost("/", async (
            StartHeroWorkflowRequest req,
            IHeroWorkflowService heroSvc,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.WorkflowType) || string.IsNullOrWhiteSpace(req.Title))
                return Results.BadRequest(new { error = "WorkflowType and Title are required." });

            var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
            var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;
            var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";

            var instance = await heroSvc.StartAsync(
                tenantId, req.WorkflowType, req.Title,
                req.Inputs ?? new Dictionary<string, string>(),
                userId, ct);

            return Results.Created($"/api/v1/hero-workflows/{instance.Id}", instance);
        }).RequireAuthorization("GovernanceWrite");

        heroWorkflows.MapPost("/{workflowId:guid}/advance", async (
            Guid workflowId,
            AdvanceHeroWorkflowRequest? req,
            IHeroWorkflowService heroSvc,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
            var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;
            var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";

            var result = await heroSvc.AdvanceAsync(workflowId, tenantId, req?.Inputs, userId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization("GovernanceWrite");

        heroWorkflows.MapGet("/{workflowId:guid}", async (
            Guid workflowId,
            IHeroWorkflowService heroSvc,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
            var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;

            var result = await heroSvc.GetAsync(workflowId, tenantId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization("GovernanceRead");

        heroWorkflows.MapGet("/", async (
            IHeroWorkflowService heroSvc,
            HttpContext httpContext,
            string? workflowType,
            string? status,
            int? limit,
            CancellationToken ct) =>
        {
            var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
            var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;
            var parsedStatus = Enum.TryParse<HeroWorkflowStatus>(status, true, out var s)
                ? s : (HeroWorkflowStatus?)null;

            var results = await heroSvc.ListAsync(tenantId, workflowType, parsedStatus, limit ?? 50, ct);
            return Results.Ok(results);
        }).RequireAuthorization("GovernanceRead");

        heroWorkflows.MapPost("/{workflowId:guid}/cancel", async (
            Guid workflowId,
            IHeroWorkflowService heroSvc,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
            var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;
            var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";

            var result = await heroSvc.CancelAsync(workflowId, tenantId, userId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization("GovernanceWrite");
    }

    private static void MapPolicySimulationEndpoints(IEndpointRouteBuilder v1)
    {
        var policySimulation = v1.MapGroup("/policy-simulation")
            .WithTags("policy-simulation")
            .RequireRateLimiting("api");

        policySimulation.MapPost("/simulate", async (
            RunSimulationRequest req,
            IPolicySimulationService simSvc,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.ActionType) || string.IsNullOrWhiteSpace(req.Title))
                return Results.BadRequest(new { error = "ActionType and Title are required." });

            var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
            var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;
            var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";

            var request = new SimulationRequest(
                TenantId: tenantId,
                ActionType: req.ActionType,
                ActionScope: req.ActionScope ?? req.ActionType,
                Title: req.Title,
                Domain: req.Domain,
                Objective: req.Objective,
                RiskLevel: req.RiskLevel,
                Reversibility: req.Reversibility,
                Confidence: req.Confidence,
                ExpectedValue: req.ExpectedValue,
                RevenueImpactLow: req.RevenueImpactLow,
                RevenueImpactHigh: req.RevenueImpactHigh,
                CostImpactLow: req.CostImpactLow,
                CostImpactHigh: req.CostImpactHigh,
                DownsideRisk: req.DownsideRisk,
                UpsidePotential: req.UpsidePotential,
                RequestedTier: req.RequestedTier,
                WorkflowType: req.WorkflowType,
                RequestedBy: userId);

            var result = await simSvc.SimulateAsync(request, ct);
            return Results.Ok(result);
        }).RequireAuthorization("GovernanceRead");

        policySimulation.MapGet("/{simulationId:guid}", async (
            Guid simulationId,
            IPolicySimulationService simSvc,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
            var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;

            var result = await simSvc.GetAsync(simulationId, tenantId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization("GovernanceRead");

        policySimulation.MapGet("/", async (
            IPolicySimulationService simSvc,
            HttpContext httpContext,
            int? limit,
            CancellationToken ct) =>
        {
            var tenantClaim = httpContext.User.FindFirst("tenant_id")?.Value;
            var tenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : Guid.Empty;

            var results = await simSvc.ListAsync(tenantId, limit ?? 50, ct);
            return Results.Ok(results);
        }).RequireAuthorization("GovernanceRead");
    }

    private static void MapInspectionEndpoints(IEndpointRouteBuilder v1)
    {
        var inspection = v1.MapGroup("/inspection")
            .WithTags("inspection")
            .RequireRateLimiting("api");

        inspection.MapGet("/summaries", async (
            IInspectionService inspectionSvc,
            HttpContext ctx,
            string? subjectType,
            string? domain,
            int? limit,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var results = await inspectionSvc.ListInspectionSummariesAsync(
                tenantId, subjectType, domain, limit ?? 50, ct);
            return Results.Ok(results);
        }).RequireAuthorization("GovernanceRead");

        inspection.MapGet("/decisions/{decisionId:guid}/rationale", async (
            Guid decisionId,
            IInspectionService inspectionSvc,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var bundle = await inspectionSvc.InspectDecisionRationaleAsync(decisionId, tenantId, ct);
            return bundle is null ? Results.NotFound() : Results.Ok(bundle);
        }).RequireAuthorization("GovernanceRead");

        inspection.MapGet("/policy/{subjectType}/{subjectId}", async (
            string subjectType,
            string subjectId,
            IInspectionService inspectionSvc,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var result = await inspectionSvc.InspectPolicyEvaluationAsync(subjectType, subjectId, tenantId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization("GovernanceRead");

        inspection.MapGet("/memory/{subjectType}/{subjectId}", async (
            string subjectType,
            string subjectId,
            IInspectionService inspectionSvc,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var refs = await inspectionSvc.InspectMemoryReferencesAsync(subjectType, subjectId, tenantId, ct);
            return Results.Ok(refs);
        }).RequireAuthorization("GovernanceRead");

        inspection.MapGet("/workflows/{workflowId:guid}/diagnostics", async (
            Guid workflowId,
            IInspectionService inspectionSvc,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var diag = await inspectionSvc.InspectWorkflowFailureAsync(workflowId, tenantId, ct);
            return diag is null ? Results.NotFound() : Results.Ok(diag);
        }).RequireAuthorization("GovernanceRead");
    }
}

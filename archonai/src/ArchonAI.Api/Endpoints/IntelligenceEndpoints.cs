using ArchonAI.Api.Dtos;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Memory;
using ArchonAI.Core.Models.OrganizationState;
using ArchonAI.Core.Models.Perception;

namespace ArchonAI.Api.Endpoints;

public static class IntelligenceEndpoints
{
    public static IEndpointRouteBuilder MapIntelligenceEndpoints(this IEndpointRouteBuilder v1)
    {
        MapMemoryEndpoints(v1);
        MapContinuousImprovementEndpoints(v1);
        MapOrgMemoryEndpoints(v1);
        MapInsightsEndpoints(v1);
        MapPerceptionEndpoints(v1);
        MapOrgStateEndpoints(v1);
        MapStrategyLearningEndpoints(v1);
        MapEnterpriseMemoryEndpoints(v1);
        return v1;
    }

    private static void MapMemoryEndpoints(IEndpointRouteBuilder v1)
    {
        var memory = v1.MapGroup("/memory")
            .RequireAuthorization("OperatorOrAdmin");

        memory.MapGet("/status", (IMemoryRetrievalOptimizer retriever) =>
            Results.Ok(retriever.GetStatus()));

        memory.MapPost("/compress", async (CompressMemoryRequest request, IMemoryCompressionEngine compressor, CancellationToken ct) =>
        {
            var result = await compressor.CompressAsync(request.Scope, ct);
            return Results.Ok(result);
        });

        memory.MapPost("/deduplicate", async (DeduplicateMemoryRequest request, IMemoryCompressionEngine compressor, CancellationToken ct) =>
        {
            int removed = await compressor.DeduplicateAsync(request.Scope, ct);
            return Results.Ok(new { scope = request.Scope, duplicatesRemoved = removed });
        });

        memory.MapPost("/cluster", async (ClusterMemoryRequest request, IMemoryCompressionEngine compressor, CancellationToken ct) =>
        {
            var clusters = await compressor.ClusterAsync(request.Scope, ct);
            return Results.Ok(clusters);
        });

        memory.MapPost("/summarize", async (SummarizeMemoryRequest request, IMemoryCompressionEngine compressor, CancellationToken ct) =>
        {
            var summary = await compressor.SummarizeAsync(request.Scope, request.SourceRecordIds, ct);
            return Results.Ok(summary);
        });

        memory.MapPost("/search", async (SearchMemoryRequest request, IMemoryRetrievalOptimizer retriever, CancellationToken ct) =>
        {
            var result = await retriever.SearchAsync(request.Scope, request.QueryEmbedding, request.TopK ?? 10, ct);
            return Results.Ok(result);
        });

        memory.MapPost("/index/rebuild", async (RebuildIndexRequest request, IMemoryRetrievalOptimizer retriever, CancellationToken ct) =>
        {
            await retriever.RebuildIndexAsync(request.Scope, ct);
            return Results.Ok(new { scope = request.Scope, rebuilt = true });
        });
    }

    private static void MapContinuousImprovementEndpoints(IEndpointRouteBuilder v1)
    {
        var continuousImprovement = v1.MapGroup("/continuous-improvement")
            .RequireAuthorization("OperatorOrAdmin");

        continuousImprovement.MapPost("/cycle", async (IContinuousImprovementEngine engine, CancellationToken ct) =>
        {
            var report = await engine.RunCycleAsync(ct);
            return Results.Ok(report);
        });

        continuousImprovement.MapGet("/inefficiencies", async (IContinuousImprovementEngine engine, CancellationToken ct) =>
        {
            var inefficiencies = await engine.DetectInefficienciesAsync(ct);
            return Results.Ok(inefficiencies);
        });

        continuousImprovement.MapPost("/recommend", async (IContinuousImprovementEngine engine, CancellationToken ct) =>
        {
            var inefficiencies = await engine.DetectInefficienciesAsync(ct);
            var recommendations = await engine.RecommendImprovementsAsync(inefficiencies, ct);
            return Results.Ok(new { inefficiencies = inefficiencies.Count, recommendations });
        });

        continuousImprovement.MapGet("/trends", async (IContinuousImprovementEngine engine, CancellationToken ct) =>
        {
            var trends = await engine.GetTrendsAsync(ct);
            return Results.Ok(trends);
        });

        continuousImprovement.MapGet("/history", (IContinuousImprovementEngine engine) =>
        {
            var history = engine.GetCycleHistory();
            return Results.Ok(history);
        });
    }

    private static void MapOrgMemoryEndpoints(IEndpointRouteBuilder v1)
    {
        var orgMemory = v1.MapGroup("/org-memory")
            .RequireAuthorization("OperatorOrAdmin");

        orgMemory.MapPost("/store", async (OrganizationalMemoryEntry entry, IOrganizationalMemoryStore store, CancellationToken ct) =>
        {
            await store.StoreAsync(entry, ct);
            return Results.Ok(new { stored = true, entryId = entry.EntryId });
        });

        orgMemory.MapPost("/search", async (OrgMemorySearchRequest request, IOrganizationalMemoryStore store, CancellationToken ct) =>
        {
            OrganizationalMemoryType? filterType = null;
            if (!string.IsNullOrWhiteSpace(request.FilterType)
                && Enum.TryParse<OrganizationalMemoryType>(request.FilterType, ignoreCase: true, out var parsed))
            {
                filterType = parsed;
            }
            var result = await store.SearchAsync(request.QueryText, filterType, request.FilterCategory, request.TopK ?? 10, ct);
            return Results.Ok(result);
        });

        orgMemory.MapGet("/timeline/{entryType}", async (string entryType, string? category, int? limit, IOrganizationalMemoryStore store, CancellationToken ct) =>
        {
            if (!Enum.TryParse<OrganizationalMemoryType>(entryType, ignoreCase: true, out var parsed))
                return Results.BadRequest(new { error = $"Invalid entry type: {entryType}" });

            var timeline = await store.GetTimelineAsync(parsed, category, limit ?? 50, ct);
            return Results.Ok(timeline);
        });

        orgMemory.MapPost("/analyze", async (IOrganizationalMemoryStore store, CancellationToken ct) =>
        {
            var report = await store.AnalyzeAsync(ct);
            return Results.Ok(report);
        });

        orgMemory.MapGet("/related/{knowledgeNodeId}", async (string knowledgeNodeId, IOrganizationalMemoryStore store, CancellationToken ct) =>
        {
            var entries = await store.GetRelatedEntriesAsync(knowledgeNodeId, ct);
            return Results.Ok(entries);
        });
    }

    private static void MapInsightsEndpoints(IEndpointRouteBuilder v1)
    {
        var insights = v1.MapGroup("/insights")
            .RequireAuthorization("OperatorOrAdmin");

        insights.MapGet("/dashboard", async (ISystemInsightEngine insightEngine, CancellationToken ct) =>
        {
            var dashboard = await insightEngine.GetDashboardAsync(ct);
            return Results.Ok(dashboard);
        });

        insights.MapGet("/health", async (ISystemInsightEngine insightEngine, CancellationToken ct) =>
        {
            var health = await insightEngine.GetHealthSummaryAsync(ct);
            return Results.Ok(health);
        });

        insights.MapGet("/bottlenecks", async (ISystemInsightEngine insightEngine, CancellationToken ct) =>
        {
            var bottlenecks = await insightEngine.DetectBottlenecksAsync(ct);
            return Results.Ok(bottlenecks);
        });

        insights.MapGet("/anomalies", async (ISystemInsightEngine insightEngine, CancellationToken ct) =>
        {
            var anomalies = await insightEngine.DetectAnomaliesAsync(ct);
            return Results.Ok(anomalies);
        });

        insights.MapGet("/agents/load", async (ISystemInsightEngine insightEngine, CancellationToken ct) =>
        {
            var load = await insightEngine.GetAgentLoadAsync(ct);
            return Results.Ok(load);
        });

        insights.MapGet("/models/latency", async (ISystemInsightEngine insightEngine, CancellationToken ct) =>
        {
            var latency = await insightEngine.GetModelLatencyAsync(ct);
            return Results.Ok(latency);
        });

        insights.MapGet("/trends", async (string? component, int? limit, ISystemInsightEngine insightEngine, CancellationToken ct) =>
        {
            var trends = await insightEngine.GetTrendsAsync(component, limit ?? 60, ct);
            return Results.Ok(trends);
        });

        insights.MapPost("/record/agent-load", (RecordAgentLoadRequest request, ISystemInsightEngine insightEngine) =>
        {
            insightEngine.RecordAgentLoad(request.AgentId, request.AgentName, request.ActiveTasks,
                request.QueuedTasks, request.ExecutionTimeMs, request.CpuPercent, request.MemoryPercent);
            return Results.Ok(new { recorded = true });
        });

        insights.MapPost("/record/model-latency", (RecordModelLatencyRequest request, ISystemInsightEngine insightEngine) =>
        {
            insightEngine.RecordModelLatency(request.Provider, request.Model, request.LatencyMs, request.Success);
            return Results.Ok(new { recorded = true });
        });
    }

    private static void MapPerceptionEndpoints(IEndpointRouteBuilder v1)
    {
        var perception = v1.MapGroup("/perception")
            .RequireAuthorization("OperatorOrAdmin");

        perception.MapPost("/signals", async (IngestSignalRequest req, ISignalIngestionService ingestion, CancellationToken ct) =>
        {
            var signal = new BusinessSignal(
                SignalId: Guid.NewGuid(),
                SignalType: Enum.Parse<SignalType>(req.SignalType, true),
                SourceSystem: Enum.Parse<SourceSystem>(req.SourceSystem, true),
                EntityId: req.EntityId,
                Timestamp: req.Timestamp ?? DateTimeOffset.UtcNow,
                Payload: req.Payload);

            var result = await ingestion.IngestApiSignalAsync(signal, ct);
            return result.Accepted ? Results.Ok(result) : Results.UnprocessableEntity(result);
        });

        perception.MapPost("/signals/batch", async (IngestSignalBatchRequest req, ISignalIngestionService ingestion, CancellationToken ct) =>
        {
            var signals = req.Signals.Select(s => new BusinessSignal(
                SignalId: Guid.NewGuid(),
                SignalType: Enum.Parse<SignalType>(s.SignalType, true),
                SourceSystem: Enum.Parse<SourceSystem>(s.SourceSystem, true),
                EntityId: s.EntityId,
                Timestamp: s.Timestamp ?? DateTimeOffset.UtcNow,
                Payload: s.Payload)).ToList();

            var source = Enum.Parse<SourceSystem>(req.Signals[0].SourceSystem, true);
            var results = await ingestion.IngestScheduledPollSignalsAsync(source, signals, ct);
            return Results.Ok(results);
        });

        perception.MapGet("/dashboard", async (IBusinessPerceptionEngine engine, CancellationToken ct) =>
        {
            var dashboard = await engine.GetDashboardAsync(ct);
            return Results.Ok(dashboard);
        });

        perception.MapPost("/polling/start", async (ISignalIngestionService ingestion, CancellationToken ct) =>
        {
            await ingestion.StartPollingAsync(ct);
            return Results.Ok(new { polling = "started" });
        }).RequireAuthorization("AdminOnly");

        perception.MapPost("/polling/stop", async (ISignalIngestionService ingestion, CancellationToken ct) =>
        {
            await ingestion.StopPollingAsync(ct);
            return Results.Ok(new { polling = "stopped" });
        }).RequireAuthorization("AdminOnly");
    }

    private static void MapOrgStateEndpoints(IEndpointRouteBuilder v1)
    {
        var orgState = v1.MapGroup("/organization-state")
            .RequireAuthorization("OperatorOrAdmin");

        orgState.MapGet("", async (IOrganizationStateEngine engine, CancellationToken ct) =>
        {
            var state = await engine.GetCurrentStateAsync(ct);
            return Results.Ok(state);
        });

        orgState.MapGet("/dashboard", async (IOrganizationStateEngine engine, CancellationToken ct) =>
        {
            var dashboard = await engine.GetDashboardAsync(ct);
            return Results.Ok(dashboard);
        });

        orgState.MapGet("/departments/{departmentId}", async (string departmentId, IOrganizationStateEngine engine, CancellationToken ct) =>
        {
            var dept = await engine.GetDepartmentStateAsync(departmentId, ct);
            return dept is null ? Results.NotFound() : Results.Ok(dept);
        });

        orgState.MapGet("/departments/{departmentId}/resources", async (string departmentId, IOrganizationStateEngine engine, CancellationToken ct) =>
        {
            var resources = await engine.GetResourcesByDepartmentAsync(departmentId, ct);
            return Results.Ok(resources);
        });

        orgState.MapGet("/customers/{customerId}", async (string customerId, IOrganizationStateEngine engine, CancellationToken ct) =>
        {
            var customer = await engine.GetCustomerStateAsync(customerId, ct);
            return customer is null ? Results.NotFound() : Results.Ok(customer);
        });

        orgState.MapGet("/customers/by-health/{status}", async (string status, IOrganizationStateEngine engine, CancellationToken ct) =>
        {
            var healthStatus = Enum.Parse<CustomerHealthStatus>(status, true);
            var customers = await engine.GetCustomersByHealthAsync(healthStatus, ct);
            return Results.Ok(customers);
        });

        orgState.MapPost("/snapshots", async (IOrganizationStateEngine engine, CancellationToken ct) =>
        {
            var snapshot = await engine.TakeSnapshotAsync(ct);
            return Results.Ok(new { snapshotId = snapshot.SnapshotId, version = snapshot.Version });
        }).RequireAuthorization("AdminOnly");
    }

    private static void MapStrategyLearningEndpoints(IEndpointRouteBuilder v1)
    {
        var strategyLearning = v1.MapGroup("/strategy-learning")
            .RequireAuthorization("OperatorOrAdmin");

        strategyLearning.MapPost("/analyze", async (IStrategyLearningEngine learningEngine, CancellationToken ct) =>
        {
            var report = await learningEngine.AnalyzeAndLearnAsync(ct);
            return Results.Ok(report);
        });

        strategyLearning.MapGet("/strategy-profiles", async (IStrategyLearningEngine learningEngine, CancellationToken ct) =>
        {
            var profiles = await learningEngine.GetStrategyProfilesAsync(ct);
            return Results.Ok(profiles);
        });

        strategyLearning.MapGet("/agent-type-profiles", async (IStrategyLearningEngine learningEngine, CancellationToken ct) =>
        {
            var profiles = await learningEngine.GetAgentTypeProfilesAsync(ct);
            return Results.Ok(profiles);
        });

        strategyLearning.MapGet("/recommend/{department}/{priority}", async (
            string department, string priority,
            IStrategyLearningEngine learningEngine, CancellationToken ct) =>
        {
            var strategy = await learningEngine.RecommendStrategyAsync(department, priority, ct);
            return Results.Ok(new { department, priority, recommendedStrategy = strategy });
        });
    }

    private static void MapEnterpriseMemoryEndpoints(IEndpointRouteBuilder v1)
    {
        var enterpriseMemory = v1.MapGroup("/enterprise-memory")
            .WithTags("enterprise-memory");

        enterpriseMemory.MapPost("/", async (
            StoreEnterpriseMemoryRequest req,
            HttpContext ctx,
            IEnterpriseMemoryService memorySvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            var userId = ctx.User?.FindFirst("sub")?.Value ?? "unknown";
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            if (!Enum.TryParse<MemoryLayer>(req.Layer, true, out var layer))
                return Results.BadRequest(new { error = $"Invalid layer: {req.Layer}." });

            var links = req.LinkedEntities?.Select(e =>
                new MemoryEntityLink(e.EntityType, e.EntityId, e.Relationship)).ToList()
                ?? new List<MemoryEntityLink>();

            var record = new EnterpriseMemoryRecord(
                Id: Guid.NewGuid(),
                TenantId: tenantId,
                Layer: layer,
                Category: req.Category ?? "",
                Subject: req.Subject,
                Content: req.Content,
                Metadata: req.Metadata?.AsReadOnly() ?? new Dictionary<string, string>().AsReadOnly(),
                LinkedEntities: links,
                Tags: req.Tags ?? Array.Empty<string>(),
                Importance: req.Importance ?? 0.5,
                CreatedBy: userId,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                ExpiresAtUtc: req.ExpiresAtUtc);

            var created = await memorySvc.StoreAsync(record, ct);
            return Results.Created($"/api/v1/enterprise-memory/{created.Id}", created);
        }).RequireAuthorization("OperatorOrAdmin");

        enterpriseMemory.MapGet("/{recordId:guid}", async (
            Guid recordId,
            HttpContext ctx,
            IEnterpriseMemoryService memorySvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var record = await memorySvc.GetAsync(recordId, tenantId, ct);
            return record is null ? Results.NotFound() : Results.Ok(record);
        }).RequireAuthorization("GovernanceRead");

        enterpriseMemory.MapGet("/", async (
            string? layer, string? category, string? tag, int? limit,
            HttpContext ctx,
            IEnterpriseMemoryService memorySvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            MemoryLayer? parsedLayer = null;
            if (layer is not null && Enum.TryParse<MemoryLayer>(layer, true, out var l))
                parsedLayer = l;

            var result = await memorySvc.QueryAsync(tenantId, parsedLayer, category, tag, limit ?? 50, ct);
            return Results.Ok(result);
        }).RequireAuthorization("GovernanceRead");

        enterpriseMemory.MapGet("/entity/{entityType}/{entityId}", async (
            string entityType, string entityId,
            HttpContext ctx,
            IEnterpriseMemoryService memorySvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var view = await memorySvc.GetEntityMemoryAsync(tenantId, entityType, entityId, ct);
            return Results.Ok(view);
        }).RequireAuthorization("GovernanceRead");

        enterpriseMemory.MapGet("/timeline", async (
            string? layer, int? limit,
            HttpContext ctx,
            IEnterpriseMemoryService memorySvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            MemoryLayer? parsedLayer = null;
            if (layer is not null && Enum.TryParse<MemoryLayer>(layer, true, out var l))
                parsedLayer = l;

            var timeline = await memorySvc.GetTimelineAsync(tenantId, parsedLayer, limit ?? 100, ct);
            return Results.Ok(timeline);
        }).RequireAuthorization("GovernanceRead");

        enterpriseMemory.MapDelete("/{recordId:guid}", async (
            Guid recordId,
            HttpContext ctx,
            IEnterpriseMemoryService memorySvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var deleted = await memorySvc.DeleteAsync(recordId, tenantId, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).RequireAuthorization("GovernanceWrite");

        enterpriseMemory.MapPost("/expire-sessions", async (
            int? maxAgeMinutes,
            HttpContext ctx,
            IEnterpriseMemoryService memorySvc,
            CancellationToken ct) =>
        {
            var tenantClaim = ctx.User?.FindFirst("tenant_id")?.Value;
            if (tenantClaim is null) return Results.Unauthorized();
            if (!Guid.TryParse(tenantClaim, out var tenantId))
                return Results.BadRequest(new { error = "Invalid tenant_id." });

            var maxAge = TimeSpan.FromMinutes(maxAgeMinutes ?? 240);
            var expired = await memorySvc.ExpireSessionMemoryAsync(tenantId, maxAge, ct);
            return Results.Ok(new { expiredCount = expired });
        }).RequireAuthorization("GovernanceWrite");
    }
}

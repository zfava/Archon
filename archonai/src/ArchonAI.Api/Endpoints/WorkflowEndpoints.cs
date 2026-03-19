using ArchonAI.Api.Dtos;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Workflow;
using ArchonAI.WorkflowRuntime;

namespace ArchonAI.Api.Endpoints;

public static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder v1)
    {
        MapOperationsEndpoints(v1);
        MapFinanceEndpoints(v1);
        MapSalesEndpoints(v1);
        MapMarketingEndpoints(v1);
        MapSupportEndpoints(v1);
        MapWorkflowCrudEndpoints(v1);
        MapDesignerEndpoints(v1);
        MapExecutionEndpoints(v1);
        MapWorkflowSimulationEndpoints(v1);
        return v1;
    }

    private static void MapOperationsEndpoints(IEndpointRouteBuilder v1)
    {
        var ops = v1.MapGroup("/operations")
            .RequireAuthorization("OperatorOrAdmin");

        ops.MapGet("/status", (IOperationsEngine opsEngine) =>
        {
            var status = opsEngine.GetStatus();
            return Results.Ok(status);
        });

        ops.MapPost("/workflows/{objectiveId:guid}/analyze", async (Guid objectiveId, Dictionary<string, string>? parameters, IOperationsEngine opsEngine, CancellationToken ct) =>
        {
            var result = await opsEngine.AnalyzeWorkflowAsync(objectiveId, parameters ?? new Dictionary<string, string>(), ct);
            return Results.Ok(result);
        });

        ops.MapGet("/inefficiencies", async (string? scope, int? maxResults, IOperationsEngine opsEngine, CancellationToken ct) =>
        {
            var insights = await opsEngine.IdentifyInefficienciesAsync(scope ?? "*", maxResults ?? 20, ct);
            return Results.Ok(insights);
        });

        ops.MapPost("/workflows/{objectiveId:guid}/recommendations", async (Guid objectiveId, IOperationsEngine opsEngine, CancellationToken ct) =>
        {
            var recommendations = await opsEngine.RecommendImprovementsAsync(objectiveId, ct);
            return Results.Ok(recommendations);
        });

        ops.MapPost("/workflows/coordinate", async (WorkflowCoordinationRequest coordRequest, IOperationsEngine opsEngine, CancellationToken ct) =>
        {
            var result = await opsEngine.CoordinateWorkflowAsync(
                coordRequest.WorkflowTemplate, coordRequest.AgentCapabilities, coordRequest.Inputs, ct);
            return result.IsSuccess ? Results.Ok(result) : Results.Problem("Coordination failed", statusCode: 400);
        });
    }

    private static void MapFinanceEndpoints(IEndpointRouteBuilder v1)
    {
        var finance = v1.MapGroup("/finance")
            .RequireAuthorization("OperatorOrAdmin");

        finance.MapGet("/status", (IFinanceEngine finEngine) =>
        {
            var status = finEngine.GetStatus();
            return Results.Ok(status);
        });

        finance.MapPost("/analyze", async (FinanceAnalysisRequest analysisRequest, IFinanceEngine finEngine, CancellationToken ct) =>
        {
            var result = await finEngine.AnalyzePerformanceAsync(analysisRequest.Scope, analysisRequest.Parameters, ct);
            return Results.Ok(result);
        });

        finance.MapGet("/anomalies", async (string? scope, double? sensitivity, int? maxResults, IFinanceEngine finEngine, CancellationToken ct) =>
        {
            var anomalies = await finEngine.DetectAnomaliesAsync(scope ?? "*", sensitivity ?? 0.7, maxResults ?? 20, ct);
            return Results.Ok(anomalies);
        });

        finance.MapPost("/summary", async (FinanceSummaryRequest summaryRequest, IFinanceEngine finEngine, CancellationToken ct) =>
        {
            var summary = await finEngine.GenerateSummaryAsync(summaryRequest.Scope, summaryRequest.Period, ct);
            return Results.Ok(summary);
        });

        finance.MapPost("/budget", async (BudgetAssistRequest budgetRequest, IFinanceEngine finEngine, CancellationToken ct) =>
        {
            var result = await finEngine.AssistBudgetingAsync(budgetRequest.DepartmentId, budgetRequest.Parameters, ct);
            return result.IsSuccess ? Results.Ok(result) : Results.Problem("Budget workflow failed", statusCode: 400);
        });
    }

    private static void MapSalesEndpoints(IEndpointRouteBuilder v1)
    {
        var sales = v1.MapGroup("/sales")
            .RequireAuthorization("OperatorOrAdmin");

        sales.MapGet("/status", (ISalesEngine salesEngine) => Results.Ok(salesEngine.GetStatus()));

        sales.MapPost("/pipelines/{pipelineId}/analyze", async (string pipelineId, Dictionary<string, string>? parameters, ISalesEngine salesEngine, CancellationToken ct) =>
        {
            var result = await salesEngine.AnalyzePipelineAsync(pipelineId, parameters ?? new Dictionary<string, string>(), ct);
            return Results.Ok(result);
        });

        sales.MapGet("/pipelines/{pipelineId}/opportunities", async (string pipelineId, int? maxResults, ISalesEngine salesEngine, CancellationToken ct) =>
        {
            var opportunities = await salesEngine.PrioritizeOpportunitiesAsync(pipelineId, maxResults ?? 20, ct);
            return Results.Ok(opportunities);
        });

        sales.MapGet("/opportunities/{opportunityId}/outreach", async (string opportunityId, ISalesEngine salesEngine, CancellationToken ct) =>
        {
            var recommendations = await salesEngine.RecommendOutreachAsync(opportunityId, ct);
            return Results.Ok(recommendations);
        });

        sales.MapGet("/metrics", async (string? scope, string? period, ISalesEngine salesEngine, CancellationToken ct) =>
        {
            var metrics = await salesEngine.GetMetricsAsync(scope ?? "*", period ?? "current", ct);
            return Results.Ok(metrics);
        });
    }

    private static void MapMarketingEndpoints(IEndpointRouteBuilder v1)
    {
        var marketing = v1.MapGroup("/marketing")
            .RequireAuthorization("OperatorOrAdmin");

        marketing.MapGet("/status", (IMarketingEngine mktEngine) => Results.Ok(mktEngine.GetStatus()));

        marketing.MapPost("/campaigns/{campaignId}/analyze", async (string campaignId, Dictionary<string, string>? parameters, IMarketingEngine mktEngine, CancellationToken ct) =>
        {
            var result = await mktEngine.AnalyzeCampaignAsync(campaignId, parameters ?? new Dictionary<string, string>(), ct);
            return Results.Ok(result);
        });

        marketing.MapGet("/strategies", async (string? scope, IMarketingEngine mktEngine, CancellationToken ct) =>
        {
            var strategies = await mktEngine.RecommendStrategiesAsync(scope ?? "*", ct);
            return Results.Ok(strategies);
        });

        marketing.MapGet("/engagement", async (string? scope, string? period, IMarketingEngine mktEngine, CancellationToken ct) =>
        {
            var metrics = await mktEngine.GetEngagementMetricsAsync(scope ?? "*", period ?? "current", ct);
            return Results.Ok(metrics);
        });
    }

    private static void MapSupportEndpoints(IEndpointRouteBuilder v1)
    {
        var support = v1.MapGroup("/support")
            .RequireAuthorization("OperatorOrAdmin");

        support.MapGet("/status", (ISupportEngine supEngine) => Results.Ok(supEngine.GetStatus()));

        support.MapPost("/tickets/analyze", async (SupportAnalysisRequest analysisRequest, ISupportEngine supEngine, CancellationToken ct) =>
        {
            var result = await supEngine.AnalyzeTicketsAsync(analysisRequest.Scope, analysisRequest.Parameters, ct);
            return Results.Ok(result);
        });

        support.MapGet("/recurring-issues", async (string? scope, int? minOccurrences, ISupportEngine supEngine, CancellationToken ct) =>
        {
            var issues = await supEngine.DetectRecurringIssuesAsync(scope ?? "*", minOccurrences ?? 3, ct);
            return Results.Ok(issues);
        });

        support.MapGet("/auto-responses/{issueCategory}", async (string issueCategory, ISupportEngine supEngine, CancellationToken ct) =>
        {
            var responses = await supEngine.RecommendAutoResponsesAsync(issueCategory, ct);
            return Results.Ok(responses);
        });
    }

    private static void MapWorkflowCrudEndpoints(IEndpointRouteBuilder v1)
    {
        var workflows = v1.MapGroup("/workflows")
            .RequireAuthorization("OperatorOrAdmin");

        workflows.MapGet("/status", (IWorkflowDesignService wfService) =>
            Results.Ok(wfService.GetStatus()));

        workflows.MapGet("", async (WorkflowDesignStatus? status, int? offset, int? limit,
            IWorkflowDesignService wfService, CancellationToken ct) =>
        {
            var list = await wfService.ListWorkflowsAsync(status, offset ?? 0, limit ?? 50, ct);
            return Results.Ok(list);
        });

        workflows.MapGet("/{workflowId:guid}", async (Guid workflowId, IWorkflowDesignService wfService, CancellationToken ct) =>
        {
            var workflow = await wfService.GetWorkflowAsync(workflowId, ct);
            return workflow is null ? Results.NotFound() : Results.Ok(workflow);
        });

        workflows.MapPost("", async (CreateWorkflowRequest request, IWorkflowDesignService wfService, CancellationToken ct) =>
        {
            var steps = request.Steps.Select(s => new WorkflowStepDefinition(
                s.Order, s.Name, s.Description, s.AgentType, s.Inputs)).ToList();

            var workflow = await wfService.CreateWorkflowAsync(
                request.Name, request.Description, request.Strategy, steps, request.Metadata, ct);

            return Results.Created($"/api/v1/workflows/{workflow.Id}", workflow);
        });

        workflows.MapPost("/{workflowId:guid}/validate", async (Guid workflowId, IWorkflowDesignService wfService, CancellationToken ct) =>
        {
            var result = await wfService.ValidateWorkflowAsync(workflowId, ct);
            return Results.Ok(result);
        });

        workflows.MapPost("/{workflowId:guid}/execute", async (Guid workflowId, ExecuteWorkflowRequest request,
            IWorkflowDesignService wfService, CancellationToken ct) =>
        {
            try
            {
                var summary = await wfService.ExecuteWorkflowAsync(workflowId, request.TenantId, request.Metadata, ct);
                return Results.Ok(summary);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });

        workflows.MapDelete("/{workflowId:guid}", async (Guid workflowId, IWorkflowDesignService wfService, CancellationToken ct) =>
        {
            var deleted = await wfService.DeleteWorkflowAsync(workflowId, ct);
            return deleted ? Results.Ok(new { workflowId, deleted = true }) : Results.NotFound();
        });
    }

    private static void MapDesignerEndpoints(IEndpointRouteBuilder v1)
    {
        var designer = v1.MapGroup("/designer/workflows")
            .RequireAuthorization("OperatorOrAdmin");

        designer.MapGet("", async (string? nameFilter, int? offset, int? limit,
            IWorkflowDesignerService designerService, CancellationToken ct) =>
        {
            var list = await designerService.ListWorkflowGraphsAsync(nameFilter, offset ?? 0, limit ?? 50, ct);
            return Results.Ok(list);
        });

        designer.MapGet("/{graphId:guid}", async (Guid graphId,
            IWorkflowDesignerService designerService, CancellationToken ct) =>
        {
            var graph = await designerService.GetWorkflowGraphAsync(graphId, ct);
            return graph is null ? Results.NotFound() : Results.Ok(graph);
        });

        designer.MapPost("", async (CreateWorkflowGraphRequest request,
            IWorkflowDesignerService designerService, CancellationToken ct) =>
        {
            var nodes = request.Nodes.Select(n => new WorkflowNode(
                Guid.NewGuid(), n.Name, n.Description, n.NodeType, n.Configuration)).ToList();

            var nodeIdMap = nodes.Select((n, i) => (i, n.Id)).ToDictionary(x => x.i, x => x.Id);

            var edges = request.Edges.Select(e => new WorkflowEdge(
                Guid.NewGuid(),
                nodeIdMap.GetValueOrDefault(e.SourceNodeIndex),
                nodeIdMap.GetValueOrDefault(e.TargetNodeIndex),
                e.Label)).ToList();

            var graph = await designerService.CreateWorkflowGraphAsync(
                request.Name, request.Description, nodes, edges, request.Metadata, ct);

            return Results.Created($"/api/v1/designer/workflows/{graph.Id}", graph);
        });

        designer.MapPost("/{graphId:guid}/validate", async (Guid graphId,
            IWorkflowDesignerService designerService, CancellationToken ct) =>
        {
            var result = await designerService.ValidateWorkflowGraphAsync(graphId, ct);
            return Results.Ok(result);
        });

        designer.MapPost("/{graphId:guid}/simulate", async (Guid graphId,
            IWorkflowDesignerService designerService, CancellationToken ct) =>
        {
            try
            {
                var result = await designerService.SimulateWorkflowGraphAsync(graphId, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
        });

        designer.MapPost("/{graphId:guid}/export", async (Guid graphId, string? version,
            IWorkflowDesignerService designerService, CancellationToken ct) =>
        {
            try
            {
                var export = await designerService.ExportWorkflowGraphAsync(graphId, version ?? "1.0", ct);
                return Results.Ok(export);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
        });

        designer.MapDelete("/{graphId:guid}", async (Guid graphId,
            IWorkflowDesignerService designerService, CancellationToken ct) =>
        {
            var deleted = await designerService.DeleteWorkflowGraphAsync(graphId, ct);
            return deleted ? Results.Ok(new { graphId, deleted = true }) : Results.NotFound();
        });
    }

    private static void MapExecutionEndpoints(IEndpointRouteBuilder v1)
    {
        var executions = v1.MapGroup("/executions")
            .RequireAuthorization("OperatorOrAdmin");

        executions.MapGet("", async (
            string? tenantId,
            WorkflowExecutionStatus? status,
            int? limit,
            IWorkflowExecutionStore store,
            CancellationToken ct) =>
        {
            var list = await store.ListAsync(tenantId, status, limit ?? 50, ct);
            return Results.Ok(list);
        });

        executions.MapGet("/{workflowId:guid}", async (
            Guid workflowId,
            IWorkflowExecutionStore store,
            CancellationToken ct) =>
        {
            var record = await store.GetAsync(workflowId, ct);
            return record is null ? Results.NotFound() : Results.Ok(record);
        });

        executions.MapPost("/{workflowId:guid}/cancel", async (
            Guid workflowId,
            DurableWorkflowExecutionEngine engine,
            CancellationToken ct) =>
        {
            try
            {
                var record = await engine.CancelWorkflowAsync(workflowId, "Cancelled via API", ct);
                return Results.Ok(record);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        executions.MapPost("/{workflowId:guid}/retry", async (
            Guid workflowId,
            DurableWorkflowExecutionEngine engine,
            CancellationToken ct) =>
        {
            try
            {
                var record = await engine.RetryWorkflowAsync(workflowId, (_, _) =>
                    global::System.Threading.Tasks.Task.FromResult(new ExecutionResult(
                        Guid.Empty, false, "No executor configured for API retry",
                        new Dictionary<string, string>(), Array.Empty<string>(),
                        new[] { "Retry via API requires executor binding" },
                        DateTimeOffset.UtcNow)), ct);
                return Results.Ok(record);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        executions.MapGet("/resumable", async (
            IWorkflowExecutionStore store,
            CancellationToken ct) =>
        {
            var resumable = await store.GetResumableAsync(ct);
            return Results.Ok(resumable);
        });
    }

    private static void MapWorkflowSimulationEndpoints(IEndpointRouteBuilder v1)
    {
        var workflowSim = v1.MapGroup("/workflow-simulation")
            .RequireAuthorization("OperatorOrAdmin");

        workflowSim.MapPost("/simulate", async (
            SimulateWorkflowRequest request,
            IWorkflowSimulationService wsService, CancellationToken ct) =>
        {
            try
            {
                var result = await wsService.SimulateAsync(
                    request.WorkflowGraphId, request.StrategyId,
                    request.HistoricalOverrides, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
        });

        workflowSim.MapGet("/predict/{workflowGraphId:guid}", async (
            Guid workflowGraphId,
            IWorkflowSimulationService wsService, CancellationToken ct) =>
        {
            try
            {
                var prediction = await wsService.PredictOutcomesAsync(workflowGraphId, ct);
                return Results.Ok(prediction);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
        });

        workflowSim.MapGet("/resources/{workflowGraphId:guid}", async (
            Guid workflowGraphId, Guid? strategyId,
            IWorkflowSimulationService wsService, CancellationToken ct) =>
        {
            try
            {
                var estimate = await wsService.EstimateResourcesAsync(workflowGraphId, strategyId, ct);
                return Results.Ok(estimate);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
        });

        workflowSim.MapGet("/latency/{workflowGraphId:guid}", async (
            Guid workflowGraphId,
            IWorkflowSimulationService wsService, CancellationToken ct) =>
        {
            try
            {
                var estimate = await wsService.EstimateLatencyAsync(workflowGraphId, ct);
                return Results.Ok(estimate);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
        });

        workflowSim.MapPost("/history", async (
            RecordHistoricalExecutionRequest request,
            IWorkflowSimulationService wsService, CancellationToken ct) =>
        {
            await wsService.RecordHistoricalExecutionAsync(
                request.WorkflowGraphId, request.IsSuccess,
                request.LatencyMs, request.Cost, ct);
            return Results.Ok(new { request.WorkflowGraphId, recorded = true });
        });

        workflowSim.MapGet("/history/{workflowGraphId:guid}", async (
            Guid workflowGraphId,
            IWorkflowSimulationService wsService, CancellationToken ct) =>
        {
            var data = await wsService.GetHistoricalDataAsync(workflowGraphId, ct);
            return data is null ? Results.NotFound() : Results.Ok(data);
        });
    }
}

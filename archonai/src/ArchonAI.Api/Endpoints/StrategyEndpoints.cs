using ArchonAI.Api.Dtos;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Core.Models.Reasoning;
using ArchonAI.Core.Models.StrategyLibrary;
using ArchonAI.ModelRouter;

namespace ArchonAI.Api.Endpoints;

public static class StrategyEndpoints
{
    public static IEndpointRouteBuilder MapStrategyEndpoints(this IEndpointRouteBuilder v1)
    {
        MapStrategySimulationEndpoints(v1);
        MapStrategyLibraryEndpoints(v1);
        MapGoalsEndpoints(v1);
        MapEconomicsEndpoints(v1);
        MapOutcomeEvaluationEndpoints(v1);
        MapModelRoutingEndpoints(v1);
        MapOnboardingEndpoints(v1);
        MapTaskGraphEndpoints(v1);
        MapExplanationEndpoints(v1);
        return v1;
    }

    private static void MapStrategySimulationEndpoints(IEndpointRouteBuilder v1)
    {
        var strategySim = v1.MapGroup("/strategy-simulation")
            .RequireAuthorization("OperatorOrAdmin");

        strategySim.MapPost("/simulate", async (SimulateStrategyRequest req, IStrategySimulator simulator, ITaskGraphBuilder graphBuilder, CancellationToken ct) =>
        {
            var graph = await graphBuilder.GetGraphAsync(req.GraphId, ct);
            if (graph is null)
                return Results.NotFound(new { error = $"Task graph '{req.GraphId}' not found." });

            var result = await simulator.SimulateAsync(graph, req.Strategy, ct);
            return Results.Ok(result);
        });

        strategySim.MapPost("/compare", async (CompareTaskGraphStrategiesRequest req, IStrategySimulator simulator, ITaskGraphBuilder graphBuilder, CancellationToken ct) =>
        {
            var graph = await graphBuilder.GetGraphAsync(req.GraphId, ct);
            if (graph is null)
                return Results.NotFound(new { error = $"Task graph '{req.GraphId}' not found." });

            var result = await simulator.CompareStrategiesAsync(graph, req.Strategies, ct);
            return Results.Ok(result);
        });

        strategySim.MapPost("/simulate-goal", async (SimulateGoalStrategiesRequest req, IStrategySimulator simulator, IGoalGenerator goalGenerator, CancellationToken ct) =>
        {
            var goal = await goalGenerator.GetGoalAsync(req.GoalId, ct);
            if (goal is null)
                return Results.NotFound(new { error = $"Goal '{req.GoalId}' not found." });

            var result = await simulator.SimulateGoalStrategiesAsync(goal, req.Strategies, ct);
            return Results.Ok(result);
        });

        strategySim.MapPost("/guided-plan", async (SimulationGuidedPlanRequest req, IStrategicPlanner planner, IGoalGenerator goalGenerator, CancellationToken ct) =>
        {
            var goal = await goalGenerator.GetGoalAsync(req.GoalId, ct);
            if (goal is null)
                return Results.NotFound(new { error = $"Goal '{req.GoalId}' not found." });

            var plan = await planner.BuildSimulationGuidedPlanAsync(goal, req.Strategies, ct);
            return Results.Ok(plan);
        });
    }

    private static void MapStrategyLibraryEndpoints(IEndpointRouteBuilder v1)
    {
        var strategyLib = v1.MapGroup("/strategy-library")
            .RequireAuthorization("OperatorOrAdmin");

        strategyLib.MapGet("/strategies", async (
            string? objectiveType, string? tag, int? offset, int? limit,
            IStrategyLibraryService slService, CancellationToken ct) =>
        {
            var strategies = await slService.ListStrategiesAsync(objectiveType, tag, offset ?? 0, limit ?? 50, ct);
            return Results.Ok(strategies);
        });

        strategyLib.MapGet("/strategies/{strategyId:guid}", async (
            Guid strategyId, IStrategyLibraryService slService, CancellationToken ct) =>
        {
            var strategy = await slService.GetStrategyAsync(strategyId, ct);
            return strategy is null ? Results.NotFound() : Results.Ok(strategy);
        });

        strategyLib.MapPost("/strategies", async (
            CreateStrategyTemplateRequest request,
            IStrategyLibraryService slService, CancellationToken ct) =>
        {
            var resourceUsage = new StrategyResourceUsage(
                request.ResourceUsage.EstimatedCpuSeconds,
                request.ResourceUsage.EstimatedMemoryMb,
                request.ResourceUsage.EstimatedAgentCount,
                request.ResourceUsage.EstimatedCostPerExecution,
                request.ResourceUsage.CostCurrency);

            var strategy = await slService.CreateStrategyAsync(
                request.Name, request.Description, request.ObjectiveType,
                request.WorkflowTemplate, request.SuccessMetrics,
                resourceUsage, request.Tags, request.Metadata, ct);
            return Results.Created($"/api/v1/strategy-library/strategies/{strategy.Id}", strategy);
        });

        strategyLib.MapPut("/strategies/{strategyId:guid}", async (
            Guid strategyId, UpdateStrategyTemplateRequest request,
            IStrategyLibraryService slService, CancellationToken ct) =>
        {
            try
            {
                StrategyResourceUsage? resourceUsage = request.ResourceUsage is not null
                    ? new StrategyResourceUsage(
                        request.ResourceUsage.EstimatedCpuSeconds,
                        request.ResourceUsage.EstimatedMemoryMb,
                        request.ResourceUsage.EstimatedAgentCount,
                        request.ResourceUsage.EstimatedCostPerExecution,
                        request.ResourceUsage.CostCurrency)
                    : null;

                var strategy = await slService.UpdateStrategyAsync(
                    strategyId, request.Description, request.WorkflowTemplate,
                    request.SuccessMetrics, resourceUsage, request.Tags, ct);
                return Results.Ok(strategy);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
        });

        strategyLib.MapDelete("/strategies/{strategyId:guid}", async (
            Guid strategyId, IStrategyLibraryService slService, CancellationToken ct) =>
        {
            try
            {
                await slService.DeleteStrategyAsync(strategyId, ct);
                return Results.Ok(new { strategyId, deleted = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
        });

        strategyLib.MapPost("/strategies/{strategyId:guid}/executions", async (
            Guid strategyId, RecordStrategyExecutionRequest request,
            IStrategyLibraryService slService, CancellationToken ct) =>
        {
            try
            {
                var record = await slService.RecordExecutionAsync(
                    strategyId, request.IsSuccess, request.LatencyMs,
                    request.Cost, request.Outcomes, ct);
                return Results.Created(
                    $"/api/v1/strategy-library/strategies/{strategyId}/executions", record);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
        });

        strategyLib.MapGet("/ranked", async (
            string objectiveType, int? limit,
            IStrategyLibraryService slService, CancellationToken ct) =>
        {
            var ranked = await slService.GetRankedStrategiesAsync(objectiveType, limit ?? 10, ct);
            return Results.Ok(ranked);
        });

        strategyLib.MapPost("/compare", async (
            CompareStrategiesRequest request,
            IStrategyLibraryService slService, CancellationToken ct) =>
        {
            try
            {
                var result = await slService.CompareStrategiesAsync(request.StrategyIds, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });
    }

    private static void MapGoalsEndpoints(IEndpointRouteBuilder v1)
    {
        var goals = v1.MapGroup("/goals")
            .RequireAuthorization("OperatorOrAdmin");

        goals.MapPost("/generate", async (IGoalGenerator generator, CancellationToken ct) =>
        {
            var result = await generator.GenerateGoalsAsync(ct);
            return Results.Ok(result);
        }).RequireAuthorization("AdminOnly");

        goals.MapGet("/dashboard", async (IGoalGenerator generator, CancellationToken ct) =>
        {
            var dashboard = await generator.GetDashboardAsync(ct);
            return Results.Ok(dashboard);
        });

        goals.MapGet("/{goalId:guid}", async (Guid goalId, IGoalGenerator generator, CancellationToken ct) =>
        {
            var goal = await generator.GetGoalAsync(goalId, ct);
            return goal is null ? Results.NotFound() : Results.Ok(goal);
        });

        goals.MapGet("/by-status/{status}", async (string status, IGoalGenerator generator, CancellationToken ct) =>
        {
            var goalStatus = Enum.Parse<GoalStatus>(status, true);
            var result = await generator.GetGoalsByStatusAsync(goalStatus, ct);
            return Results.Ok(result);
        });

        goals.MapPost("/{goalId:guid}/approve", async (Guid goalId, IGoalGenerator generator, CancellationToken ct) =>
        {
            await generator.ApproveGoalAsync(goalId, ct);
            return Results.Ok(new { goalId, status = "approved" });
        }).RequireAuthorization("AdminOnly");

        goals.MapPost("/{goalId:guid}/cancel", async (Guid goalId, CancelGoalRequest req, IGoalGenerator generator, CancellationToken ct) =>
        {
            await generator.CancelGoalAsync(goalId, req.Reason, ct);
            return Results.Ok(new { goalId, status = "cancelled" });
        }).RequireAuthorization("AdminOnly");
    }

    private static void MapEconomicsEndpoints(IEndpointRouteBuilder v1)
    {
        var economics = v1.MapGroup("/economics")
            .RequireAuthorization("OperatorOrAdmin");

        economics.MapPost("/evaluate", async (EconomicEvaluationRequest req, IEconomicEvaluator evaluator, IStrategicPlanner planner, CancellationToken ct) =>
        {
            var objective = new Objective(
                Id: Guid.NewGuid(),
                Title: req.ObjectiveTitle,
                Description: req.ObjectiveDescription,
                Constraints: req.Constraints ?? new Dictionary<string, string>(),
                CreatedAtUtc: DateTimeOffset.UtcNow,
                DueAtUtc: req.Deadline);

            var workflow = await planner.BuildWorkflowAsync(objective, ct);

            var strategies = req.CandidateStrategies is { Count: > 0 }
                ? req.CandidateStrategies
                : (IReadOnlyList<string>)new[] { "balanced", "safe-mode", "throughput-optimized", "cost-optimized" };

            var weights = req.Weights is not null
                ? new EconomicWeights(req.Weights.CostWeight, req.Weights.ImpactWeight, req.Weights.SuccessProbabilityWeight, req.Weights.ExecutionTimeWeight)
                : null;

            var result = await evaluator.EvaluateStrategiesAsync(objective, workflow, strategies, weights, ct);
            return Results.Ok(result);
        });

        economics.MapPost("/evaluate-single", async (SingleStrategyEvaluationRequest req, IEconomicEvaluator evaluator, IStrategicPlanner planner, CancellationToken ct) =>
        {
            var objective = new Objective(
                Id: Guid.NewGuid(),
                Title: req.ObjectiveTitle,
                Description: req.ObjectiveDescription,
                Constraints: req.Constraints ?? new Dictionary<string, string>(),
                CreatedAtUtc: DateTimeOffset.UtcNow,
                DueAtUtc: req.Deadline);

            var workflow = await planner.BuildWorkflowAsync(objective, ct);
            var result = await evaluator.EvaluateSingleStrategyAsync(objective, workflow, req.Strategy, ct);
            return Results.Ok(result);
        });
    }

    private static void MapOutcomeEvaluationEndpoints(IEndpointRouteBuilder v1)
    {
        var outcomeEval = v1.MapGroup("/outcome-evaluation")
            .RequireAuthorization("OperatorOrAdmin");

        outcomeEval.MapPost("/evaluate", async (EvaluateOutcomeRequest req, IOutcomeEvaluator evaluator, IStrategySimulator simulator, ITaskGraphBuilder graphBuilder, CancellationToken ct) =>
        {
            var graph = await graphBuilder.GetGraphAsync(req.SimulationResult.GraphId, ct);
            if (graph is null)
                return Results.NotFound(new { error = $"Task graph '{req.SimulationResult.GraphId}' not found." });

            var result = await evaluator.EvaluateAsync(req.SimulationResult, req.ActualResult, ct);
            return Results.Ok(result);
        });

        outcomeEval.MapGet("/goal/{goalId:guid}", async (Guid goalId, IOutcomeEvaluator evaluator, CancellationToken ct) =>
        {
            var evaluations = await evaluator.GetEvaluationsForGoalAsync(goalId, ct);
            return Results.Ok(evaluations);
        });

        outcomeEval.MapGet("/strategy/{strategy}", async (string strategy, IOutcomeEvaluator evaluator, CancellationToken ct) =>
        {
            var evaluations = await evaluator.GetEvaluationsForStrategyAsync(strategy, ct);
            return Results.Ok(evaluations);
        });
    }

    private static void MapModelRoutingEndpoints(IEndpointRouteBuilder v1)
    {
        var modelRouting = v1.MapGroup("/model-routing")
            .RequireAuthorization("OperatorOrAdmin");

        modelRouting.MapPost("/adjust-weights", async (AdaptiveRoutingWeightEngine engine, CancellationToken ct) =>
        {
            var report = await engine.AdjustWeightsAsync(ct);
            return Results.Ok(report);
        });

        modelRouting.MapGet("/weights", (IModelPerformanceTracker tracker) =>
        {
            var weights = tracker.GetRoutingWeights();
            return Results.Ok(weights);
        });

        modelRouting.MapGet("/weights/{taskType}", (string taskType, IModelPerformanceTracker tracker) =>
        {
            var weights = tracker.GetTaskTypeWeights(taskType);
            return Results.Ok(weights);
        });

        modelRouting.MapGet("/scores", (IModelPerformanceTracker tracker) =>
        {
            var scores = tracker.GetAllScores();
            return Results.Ok(scores);
        });

        modelRouting.MapGet("/scores/{provider}/{model}", (string provider, string model, IModelPerformanceTracker tracker) =>
        {
            var score = tracker.GetScore(provider, model);
            return score is not null ? Results.Ok(score) : Results.NotFound();
        });

        modelRouting.MapPost("/select", (
            string strategy,
            string? taskType,
            IModelPerformanceTracker tracker) =>
        {
            var selected = tracker.SelectByWeight(strategy, taskType);
            if (selected is null)
                return Results.NotFound(new { message = "No eligible models found" });

            var weight = tracker.GetRoutingWeight(selected.Provider, selected.Model);
            return Results.Ok(new
            {
                selected.Provider,
                selected.Model,
                selected.CompositeScore,
                selected.SuccessRate,
                selected.AverageLatencyMs,
                selected.AverageCostPerRequest,
                RoutingWeight = weight?.Weight
            });
        });

        modelRouting.MapGet("/capabilities", () =>
        {
            return Results.Ok(ArchonAI.Core.Models.Models.ModelCapabilityRegistry.All);
        });

        modelRouting.MapGet("/providers/status", (IModelProvider provider) =>
        {
            // Return provider type info — actual health requires a live call
            return Results.Ok(new
            {
                providerType = provider.ProviderName,
                availableModels = ArchonAI.Core.Models.Models.ModelCapabilityRegistry.All.Keys
            });
        });
    }

    private static void MapOnboardingEndpoints(IEndpointRouteBuilder v1)
    {
        var onboarding = v1.MapGroup("/onboarding")
            .RequireAuthorization("OperatorOrAdmin");

        onboarding.MapPost("/deploy", async (
            ArchonAI.Core.Models.Onboarding.OnboardingDeployRequest req,
            IOnboardingService onboardingSvc,
            CancellationToken ct) =>
        {
            var result = await onboardingSvc.DeployAsync(req, ct);
            return Results.Ok(result);
        });

        onboarding.MapPost("/deploy-template", async (
            ArchonAI.Core.Models.Onboarding.OnboardingTemplateDeployRequest req,
            IOnboardingService onboardingSvc,
            CancellationToken ct) =>
        {
            var result = await onboardingSvc.DeployTemplateAsync(req, ct);
            return Results.Ok(result);
        });
    }

    private static void MapTaskGraphEndpoints(IEndpointRouteBuilder v1)
    {
        var taskGraphs = v1.MapGroup("/task-graphs")
            .RequireAuthorization("OperatorOrAdmin");

        taskGraphs.MapPost("/build", async (BuildTaskGraphRequest req, ITaskGraphBuilder builder, IGoalGenerator goalGenerator, CancellationToken ct) =>
        {
            var goal = await goalGenerator.GetGoalAsync(req.GoalId, ct);
            if (goal is null)
                return Results.NotFound(new { error = $"Goal '{req.GoalId}' not found." });

            var graph = await builder.BuildGraphAsync(goal, req.Strategy ?? "balanced", ct);
            return Results.Ok(graph);
        });

        taskGraphs.MapPost("/{graphId:guid}/dispatch", async (Guid graphId, ITaskGraphBuilder builder, CancellationToken ct) =>
        {
            var graph = await builder.GetGraphAsync(graphId, ct);
            if (graph is null)
                return Results.NotFound(new { error = $"Task graph '{graphId}' not found." });

            var result = await builder.DispatchGraphAsync(graph, ct);
            return Results.Ok(result);
        }).RequireAuthorization("AdminOnly");

        taskGraphs.MapGet("/{graphId:guid}", async (Guid graphId, ITaskGraphBuilder builder, CancellationToken ct) =>
        {
            var graph = await builder.GetGraphAsync(graphId, ct);
            return graph is null ? Results.NotFound() : Results.Ok(graph);
        });

        taskGraphs.MapGet("/by-goal/{goalId:guid}", async (Guid goalId, ITaskGraphBuilder builder, CancellationToken ct) =>
        {
            var graphs = await builder.GetGraphsByGoalAsync(goalId, ct);
            return Results.Ok(graphs);
        });

        taskGraphs.MapGet("/{graphId:guid}/layers", async (Guid graphId, ITaskGraphBuilder builder, CancellationToken ct) =>
        {
            var graph = await builder.GetGraphAsync(graphId, ct);
            if (graph is null)
                return Results.NotFound();

            var layers = graph.GetExecutionLayers();
            return Results.Ok(new
            {
                graphId,
                totalLayers = layers.Count,
                totalNodes = graph.Nodes.Count,
                layers = layers.Select((layer, index) => new
                {
                    layerIndex = index,
                    parallelNodes = layer.Select(n => new { n.NodeId, n.Name, n.AgentType, n.ExpectedOutput })
                })
            });
        });
    }

    private static void MapExplanationEndpoints(IEndpointRouteBuilder v1)
    {
        var explanations = v1.MapGroup("/explanations")
            .RequireAuthorization("OperatorOrAdmin");

        explanations.MapPost("/strategy", async (
            ExplainStrategyRequest req,
            IExplanationEngine explanationEngine,
            CancellationToken ct) =>
        {
            var result = await explanationEngine.ExplainStrategyChoiceAsync(
                req.GoalId, req.GoalTitle, req.CandidateStrategies, ct);
            return Results.Ok(result);
        });

        explanations.MapPost("/agent", async (
            ExplainAgentRequest req,
            IExplanationEngine explanationEngine,
            CancellationToken ct) =>
        {
            var result = await explanationEngine.ExplainAgentSelectionAsync(
                req.RequiredCapability, req.TaskType, ct);
            return Results.Ok(result);
        });

        explanations.MapPost("/decision", async (
            ExplainDecisionRequest req,
            IExplanationEngine explanationEngine,
            CancellationToken ct) =>
        {
            var result = await explanationEngine.ExplainDecisionAsync(
                req.GoalId, req.GoalTitle, req.CandidateStrategies,
                req.RequiredCapability, req.TaskType, ct);
            return Results.Ok(result);
        });
    }
}

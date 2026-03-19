using ArchonAI.Api.Dtos;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.AgentRegistry;
using ArchonAI.Core.Models.Planning;
using ArchonAI.Registry;

namespace ArchonAI.Api.Endpoints;

public static class RegistryEndpoints
{
    public static IEndpointRouteBuilder MapRegistryEndpoints(this IEndpointRouteBuilder v1)
    {
        var registry = v1.MapGroup("/registry")
            .RequireAuthorization("OperatorOrAdmin");

        registry.MapGet("/agents", async (IAgentCapabilityRegistry capabilityRegistry, IAgentRegistryService agentRegistryService, CancellationToken ct) =>
        {
            var agents = await capabilityRegistry.GetAllAsync(ct);
            var disabledAgents = await agentRegistryService.ListAgentsAsync(
                status: RegisteredAgentStatus.Disabled, limit: 1000, ct: ct);
            var disabledIds = disabledAgents.Select(a => a.Id).ToHashSet();

            var result = agents.Select(a => new
            {
                a.AgentId, a.AgentName, a.Version, a.Capabilities, a.Tools, a.Permissions,
                a.SupportedTaskTypes, a.AverageLatencyMs, a.P95LatencyMs, a.AverageCost,
                a.Executions, a.SuccessCount, a.FailureCount, a.SuccessRate,
                a.Throughput, a.UpdatedAtUtc, a.IsSuspended, a.SuspendReason,
                SyncWarning = !a.IsSuspended && disabledIds.Contains(a.AgentId)
                    ? "Agent is active in capability registry but disabled in agent registry — synchronization pending."
                    : (string?)null
            });
            return Results.Ok(result);
        });

        registry.MapGet("/agents/{agentId:guid}", async (Guid agentId, IAgentCapabilityRegistry capabilityRegistry, CancellationToken ct) =>
        {
            var agent = await capabilityRegistry.GetAgentAsync(agentId, ct);
            return agent is null ? Results.NotFound() : Results.Ok(agent);
        });

        registry.MapGet("/capabilities/{capability}", async (string capability, IAgentCapabilityRegistry capabilityRegistry, CancellationToken ct) =>
        {
            var matches = await capabilityRegistry.QueryByCapabilityAsync(capability, ct);
            return Results.Ok(matches);
        });

        registry.MapGet("/task-types/{taskType}", async (string taskType, IAgentCapabilityRegistry capabilityRegistry, CancellationToken ct) =>
        {
            var matches = await capabilityRegistry.QueryByTaskTypeAsync(taskType, ct);
            return Results.Ok(matches);
        });

        registry.MapGet("/select-best", async (string capability, string? taskType, IAgentCapabilityRegistry capabilityRegistry, CancellationToken ct) =>
        {
            var selection = await capabilityRegistry.SelectBestAgentAsync(capability, taskType, ct);
            return selection is null
                ? Results.NotFound(new { error = $"No agent found for capability '{capability}'." })
                : Results.Ok(selection);
        });

        registry.MapGet("/agents/{agentId:guid}/performance", async (Guid agentId, IAgentCapabilityRegistry capabilityRegistry, CancellationToken ct) =>
        {
            var snapshot = await capabilityRegistry.GetPerformanceSnapshotAsync(agentId, ct);
            return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
        });

        registry.MapPost("/agents/{agentId:guid}/task-types", async (Guid agentId, RegisterTaskTypesRequest req, IAgentCapabilityRegistry capabilityRegistry, CancellationToken ct) =>
        {
            await capabilityRegistry.RegisterSupportedTaskTypesAsync(agentId, req.TaskTypes, ct);
            var updated = await capabilityRegistry.GetAgentAsync(agentId, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }).RequireAuthorization("AdminOnly");

        var traces = v1.MapGroup("/traces")
            .RequireAuthorization("OperatorOrAdmin");

        traces.MapGet("", async (
            string? scope,
            string? category,
            int? limit,
            ITraceStore traceStore,
            CancellationToken ct) =>
        {
            var entries = await traceStore.QueryAsync(scope, category, limit ?? 200, ct);
            return Results.Ok(entries);
        });

        var patterns = v1.MapGroup("/patterns")
            .RequireAuthorization("OperatorOrAdmin");

        patterns.MapPost("/objectives/{objectiveId:guid}/analyze", async (
            Guid objectiveId,
            IPatternAnalyzer patternAnalyzer,
            CancellationToken ct) =>
        {
            var discovered = await patternAnalyzer.AnalyzeObjectiveAsync(objectiveId, ct);
            return Results.Ok(discovered);
        });

        var strategies = v1.MapGroup("/strategies")
            .RequireAuthorization("OperatorOrAdmin");

        strategies.MapGet("/{objectiveType}", async (
            string objectiveType,
            IStrategyStore strategyStore,
            CancellationToken ct) =>
        {
            var result = await strategyStore.QueryByObjectiveTypeAsync(objectiveType, ct);
            return Results.Ok(result);
        });

        strategies.MapPost("", async (
            OperationalStrategy strategy,
            IStrategyStore strategyStore,
            CancellationToken ct) =>
        {
            await strategyStore.SaveAsync(strategy, ct);
            return Results.Accepted();
        });

        return v1;
    }
}

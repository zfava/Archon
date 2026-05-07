using ArchonAI.Api.Dtos;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.AgentRegistry;

namespace ArchonAI.Api.Endpoints;

public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder v1)
    {
        // ── Agent Coordination ──────────────────────────────────────
        var coordination = v1.MapGroup("/coordination")
            .RequireAuthorization("OperatorOrAdmin");

        coordination.MapGet("/status", (IAgentCoordinationService coordService) =>
            Results.Ok(coordService.GetStatus()));

        coordination.MapPost("/support", async (RequestTaskSupportInput input, IAgentCoordinationService coordService, CancellationToken ct) =>
        {
            var request = new ArchonAI.Core.Models.Coordination.TaskSupportRequest(
                Id: Guid.NewGuid(),
                RequestingAgentId: input.RequestingAgentId,
                RequestingAgentName: input.RequestingAgentName,
                TaskId: input.TaskId,
                RequiredCapability: input.RequiredCapability,
                Reason: input.Reason,
                Context: input.Context ?? new Dictionary<string, string>(),
                Timeout: TimeSpan.FromSeconds(input.TimeoutSeconds ?? 60),
                RequestedAtUtc: DateTimeOffset.UtcNow);
            var response = await coordService.RequestTaskSupportAsync(request, ct);
            return Results.Ok(response);
        });

        coordination.MapPost("/knowledge", async (ShareKnowledgeInput input, IAgentCoordinationService coordService, CancellationToken ct) =>
        {
            var payload = new ArchonAI.Core.Models.Coordination.KnowledgeSharePayload(
                Id: Guid.NewGuid(),
                SourceAgentId: input.SourceAgentId,
                SourceAgentName: input.SourceAgentName,
                TargetAgentId: input.TargetAgentId,
                Topic: input.Topic,
                Content: input.Content,
                Metadata: input.Metadata ?? new Dictionary<string, string>(),
                SharedAtUtc: DateTimeOffset.UtcNow);
            await coordService.ShareKnowledgeAsync(payload, ct);
            return Results.Ok(new { payloadId = payload.Id, shared = true });
        });

        coordination.MapPost("/delegate", async (DelegateTaskInput input, IAgentCoordinationService coordService, CancellationToken ct) =>
        {
            var delegation = new ArchonAI.Core.Models.Coordination.TaskDelegation(
                Id: Guid.NewGuid(),
                DelegatingAgentId: input.DelegatingAgentId,
                DelegatingAgentName: input.DelegatingAgentName,
                TargetAgentId: input.TargetAgentId,
                TargetAgentName: input.TargetAgentName,
                OriginalTaskId: input.OriginalTaskId,
                RequiredCapability: input.RequiredCapability,
                TaskInputs: input.TaskInputs ?? new Dictionary<string, string>(),
                Timeout: TimeSpan.FromSeconds(input.TimeoutSeconds ?? 60),
                DelegatedAtUtc: DateTimeOffset.UtcNow);
            var result = await coordService.DelegateTaskAsync(delegation, ct);
            return Results.Ok(result);
        });

        // ── Agent Collaboration ─────────────────────────────────────
        var collaboration = v1.MapGroup("/collaboration")
            .RequireAuthorization("OperatorOrAdmin");

        collaboration.MapGet("/status", (IAgentCollaborationManager collabManager) =>
            Results.Ok(collabManager.GetStatus()));

        collaboration.MapPost("/sessions", async (CreateCollaborationSessionInput input, IAgentCollaborationManager collabManager, CancellationToken ct) =>
        {
            var session = await collabManager.CreateSessionAsync(
                input.InitiatorAgentId, input.InitiatorAgentName, input.Purpose, input.InitialContext, ct);
            return Results.Created($"/api/v1/collaboration/sessions/{session.SessionId}", session);
        });

        collaboration.MapGet("/sessions/{sessionId:guid}", async (Guid sessionId, IAgentCollaborationManager collabManager, CancellationToken ct) =>
        {
            var session = await collabManager.GetSessionAsync(sessionId, ct);
            return session is null ? Results.NotFound() : Results.Ok(session);
        });

        collaboration.MapPost("/sessions/{sessionId:guid}/join", async (Guid sessionId, JoinCollaborationSessionInput input, IAgentCollaborationManager collabManager, CancellationToken ct) =>
        {
            var session = await collabManager.JoinSessionAsync(sessionId, input.AgentId, input.AgentName, input.Role, ct);
            return Results.Ok(session);
        });

        collaboration.MapPost("/sessions/{sessionId:guid}/context", async (Guid sessionId, ShareCollaborationContextInput input, IAgentCollaborationManager collabManager, CancellationToken ct) =>
        {
            await collabManager.ShareContextAsync(sessionId, input.AgentId, input.Context, ct);
            var session = await collabManager.GetSessionAsync(sessionId, ct);
            return Results.Ok(session);
        });

        collaboration.MapPost("/sessions/{sessionId:guid}/complete", async (Guid sessionId, IAgentCollaborationManager collabManager, CancellationToken ct) =>
        {
            var session = await collabManager.CompleteSessionAsync(sessionId, ct);
            return Results.Ok(session);
        });

        collaboration.MapPost("/delegate-smart", async (SmartDelegationInput input, IAgentCollaborationManager collabManager, CancellationToken ct) =>
        {
            var request = new ArchonAI.Core.Models.Collaboration.SmartDelegationRequest(
                RequestId: Guid.NewGuid(),
                DelegatingAgentId: input.DelegatingAgentId,
                DelegatingAgentName: input.DelegatingAgentName,
                RequiredCapability: input.RequiredCapability,
                PreferredTaskType: input.PreferredTaskType,
                TaskInputs: input.TaskInputs ?? new Dictionary<string, string>(),
                FallbackAgentIds: input.FallbackAgentIds,
                Timeout: TimeSpan.FromSeconds(input.TimeoutSeconds ?? 120),
                RequestedAtUtc: DateTimeOffset.UtcNow);
            var result = await collabManager.DelegateSmartAsync(request, ct);
            return Results.Ok(result);
        });

        collaboration.MapPost("/assist", async (AssistanceRequestInput input, IAgentCollaborationManager collabManager, CancellationToken ct) =>
        {
            var request = new ArchonAI.Core.Models.Collaboration.AssistanceRequest(
                RequestId: Guid.NewGuid(),
                RequestingAgentId: input.RequestingAgentId,
                RequestingAgentName: input.RequestingAgentName,
                Objective: input.Objective,
                RequiredCapabilities: input.RequiredCapabilities,
                Context: input.Context ?? new Dictionary<string, string>(),
                MaxResponders: input.MaxResponders ?? 5,
                Timeout: TimeSpan.FromSeconds(input.TimeoutSeconds ?? 120),
                RequestedAtUtc: DateTimeOffset.UtcNow);
            var result = await collabManager.RequestAssistanceAsync(request, ct);
            return Results.Ok(result);
        });

        // ── Agent Registry ──────────────────────────────────────────
        var agentRegistry = v1.MapGroup("/agent-registry")
            .RequireAuthorization("OperatorOrAdmin");

        agentRegistry.MapGet("/agents", async (
            RegisteredAgentStatus? status, string? capability, int? offset, int? limit,
            IAgentRegistryService arService, CancellationToken ct) =>
        {
            var agents = await arService.ListAgentsAsync(status, capability, offset ?? 0, limit ?? 50, ct);
            return Results.Ok(agents);
        });

        agentRegistry.MapGet("/agents/{agentId:guid}", async (
            Guid agentId, IAgentRegistryService arService, CancellationToken ct) =>
        {
            var agent = await arService.GetAgentAsync(agentId, ct);
            return agent is null ? Results.NotFound() : Results.Ok(agent);
        });

        agentRegistry.MapPost("/agents", async (
            RegisterAgentRequest request, IAgentRegistryService arService, CancellationToken ct) =>
        {
            var capabilities = request.Capabilities.Select(c => new AgentCapabilityRecord(
                Id: Guid.Empty, AgentId: Guid.Empty,
                Name: c.Name, Description: c.Description,
                Category: c.Category, Version: c.Version,
                AddedAtUtc: default)).ToList();

            var agent = await arService.RegisterAgentAsync(
                request.Name, request.Description, request.Version,
                capabilities, request.Configuration, ct);
            return Results.Created($"/api/v1/agent-registry/agents/{agent.Id}", agent);
        });

        agentRegistry.MapPut("/agents/{agentId:guid}/capabilities", async (
            Guid agentId, UpdateAgentCapabilitiesRequest request,
            IAgentRegistryService arService, CancellationToken ct) =>
        {
            try
            {
                var capabilities = request.Capabilities.Select(c => new AgentCapabilityRecord(
                    Id: Guid.Empty, AgentId: agentId,
                    Name: c.Name, Description: c.Description,
                    Category: c.Category, Version: c.Version,
                    AddedAtUtc: default)).ToList();

                var agent = await arService.UpdateCapabilitiesAsync(agentId, capabilities, ct);
                return Results.Ok(agent);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
        });

        agentRegistry.MapPost("/agents/{agentId:guid}/enable", async (
            Guid agentId, IAgentRegistryService arService, CancellationToken ct) =>
        {
            try
            {
                var agent = await arService.EnableAgentAsync(agentId, ct);
                return Results.Ok(agent);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
        });

        agentRegistry.MapPost("/agents/{agentId:guid}/disable", async (
            Guid agentId, DisableAgentRequest request,
            IAgentRegistryService arService, CancellationToken ct) =>
        {
            try
            {
                var agent = await arService.DisableAgentAsync(agentId, request.Reason, ct);
                return Results.Ok(agent);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
        });

        agentRegistry.MapPost("/agents/{agentId:guid}/heartbeat", async (
            Guid agentId, IAgentRegistryService arService, CancellationToken ct) =>
        {
            try
            {
                await arService.RecordHeartbeatAsync(agentId, ct);
                return Results.Ok(new { agentId, heartbeatAtUtc = DateTimeOffset.UtcNow });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
        });

        agentRegistry.MapPost("/agents/{agentId:guid}/metrics", async (
            Guid agentId, RecordAgentMetricsRequest request,
            IAgentRegistryService arService, CancellationToken ct) =>
        {
            try
            {
                var metric = await arService.RecordMetricsAsync(
                    agentId, request.TotalExecutions, request.SuccessfulExecutions,
                    request.FailedExecutions, request.AverageLatencyMs,
                    request.P95LatencyMs, request.UptimePercent, ct);
                return Results.Created($"/api/v1/agent-registry/agents/{agentId}/metrics", metric);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
        });

        agentRegistry.MapGet("/agents/{agentId:guid}/metrics", async (
            Guid agentId, int? limit,
            IAgentRegistryService arService, CancellationToken ct) =>
        {
            var metrics = await arService.GetMetricsAsync(agentId, limit ?? 20, ct);
            return Results.Ok(metrics);
        });

        agentRegistry.MapGet("/dashboard", async (
            IAgentRegistryService arService, CancellationToken ct) =>
        {
            var dashboard = await arService.GetDashboardAsync(ct);
            return Results.Ok(dashboard);
        });

        agentRegistry.MapDelete("/agents/{agentId:guid}", async (
            Guid agentId, IAgentRegistryService arService, CancellationToken ct) =>
        {
            try
            {
                await arService.DeregisterAgentAsync(agentId, ct);
                return Results.Ok(new { agentId, deregistered = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
        });

        return v1;
    }
}

using ArchonAI.Api.Dtos;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Cluster;
using ArchonAI.Core.Models.ControlPlane;

namespace ArchonAI.Api.Endpoints;

public static class InfraEndpoints
{
    public static IEndpointRouteBuilder MapInfraEndpoints(this IEndpointRouteBuilder v1)
    {
        MapControlPlaneEndpoints(v1);
        MapStandaloneObservabilityEndpoints(v1);
        MapMonitoringEndpoints(v1);
        MapClusterEndpoints(v1);
        return v1;
    }

    private static void MapControlPlaneEndpoints(IEndpointRouteBuilder v1)
    {
        var controlPlane = v1.MapGroup("/control-plane")
            .RequireAuthorization("AdminOnly");

        controlPlane.MapGet("/status", (IControlPlaneService cpService) =>
            Results.Ok(cpService.GetStatus()));

        controlPlane.MapGet("/dashboard", async (IControlPlaneService cpService, CancellationToken ct) =>
        {
            var dashboard = await cpService.GetDashboardAsync(ct);
            return Results.Ok(dashboard);
        });

        MapTenantEndpoints(controlPlane);
        MapWorkflowEndpoints(controlPlane);
        MapAgentEndpoints(controlPlane);
        MapPolicyEndpoints(controlPlane);
        MapConfigEndpoints(controlPlane);
        MapObservabilityEndpoints(v1);
        MapSystemEndpoints(v1);
        MapAlertEndpoints(v1);
    }

    private static void MapTenantEndpoints(RouteGroupBuilder controlPlane)
    {
        var tenants = controlPlane.MapGroup("/tenants");

        tenants.MapGet("", async (TenantStatus? status, int? offset, int? limit,
            IControlPlaneService cpService, CancellationToken ct) =>
        {
            var list = await cpService.ListTenantsAsync(status, offset ?? 0, limit ?? 50, ct);
            return Results.Ok(list);
        });

        tenants.MapGet("/{tenantId:guid}", async (Guid tenantId,
            IControlPlaneService cpService, CancellationToken ct) =>
        {
            var tenant = await cpService.GetTenantAsync(tenantId, ct);
            return tenant is null ? Results.NotFound() : Results.Ok(tenant);
        });

        tenants.MapPost("", async (ProvisionTenantRequest request,
            IControlPlaneService cpService, CancellationToken ct) =>
        {
            try
            {
                var tenant = await cpService.ProvisionTenantAsync(
                    request.Name, request.DisplayName, request.Tier, request.Metadata, ct);
                return Results.Created($"/api/v1/control-plane/tenants/{tenant.Id}", tenant);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 409);
            }
        });

        tenants.MapPost("/{tenantId:guid}/activate", async (Guid tenantId,
            IControlPlaneService cpService, CancellationToken ct) =>
        {
            try
            {
                var tenant = await cpService.ActivateTenantAsync(tenantId, ct);
                return Results.Ok(tenant);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });

        tenants.MapPost("/{tenantId:guid}/suspend", async (Guid tenantId, SuspendTenantRequest request,
            IControlPlaneService cpService, CancellationToken ct) =>
        {
            try
            {
                var tenant = await cpService.SuspendTenantAsync(tenantId, request.Reason, ct);
                return Results.Ok(tenant);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });

        tenants.MapDelete("/{tenantId:guid}", async (Guid tenantId,
            IControlPlaneService cpService, CancellationToken ct) =>
        {
            try
            {
                await cpService.DeprovisionTenantAsync(tenantId, ct);
                return Results.Ok(new { tenantId, deprovisioned = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });
    }

    private static void MapWorkflowEndpoints(RouteGroupBuilder controlPlane)
    {
        var cpWorkflows = controlPlane.MapGroup("/workflows");

        cpWorkflows.MapGet("", async (string? tenantId, ManagedWorkflowStatus? status,
            int? offset, int? limit, IControlPlaneService cpService, CancellationToken ct) =>
        {
            var list = await cpService.ListManagedWorkflowsAsync(tenantId, status, offset ?? 0, limit ?? 50, ct);
            return Results.Ok(list);
        });

        cpWorkflows.MapGet("/{workflowId:guid}", async (Guid workflowId,
            IControlPlaneService cpService, CancellationToken ct) =>
        {
            var workflow = await cpService.GetManagedWorkflowAsync(workflowId, ct);
            return workflow is null ? Results.NotFound() : Results.Ok(workflow);
        });

        cpWorkflows.MapPost("", async (RegisterWorkflowRequest request,
            IControlPlaneService cpService, CancellationToken ct) =>
        {
            try
            {
                var workflow = await cpService.RegisterWorkflowAsync(
                    request.TenantId, request.Name, request.Description,
                    request.Strategy, request.StepCount, request.Metadata, ct);
                return Results.Created($"/api/v1/control-plane/workflows/{workflow.Id}", workflow);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });

        cpWorkflows.MapPatch("/{workflowId:guid}/status", async (Guid workflowId,
            UpdateManagedStatusRequest request, IControlPlaneService cpService, CancellationToken ct) =>
        {
            try
            {
                var workflow = await cpService.UpdateWorkflowStatusAsync(workflowId, request.Status, ct);
                return Results.Ok(workflow);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });
    }

    private static void MapAgentEndpoints(RouteGroupBuilder controlPlane)
    {
        var cpAgents = controlPlane.MapGroup("/agents");

        cpAgents.MapGet("", async (string? tenantId, ManagedAgentStatus? status,
            int? offset, int? limit, IControlPlaneService cpService, CancellationToken ct) =>
        {
            var list = await cpService.ListManagedAgentsAsync(tenantId, status, offset ?? 0, limit ?? 50, ct);
            return Results.Ok(list);
        });

        cpAgents.MapGet("/{agentId:guid}", async (Guid agentId,
            IControlPlaneService cpService, CancellationToken ct) =>
        {
            var agent = await cpService.GetManagedAgentAsync(agentId, ct);
            return agent is null ? Results.NotFound() : Results.Ok(agent);
        });

        cpAgents.MapPost("", async (RegisterManagedAgentRequest request,
            IControlPlaneService cpService, CancellationToken ct) =>
        {
            try
            {
                var agent = await cpService.RegisterAgentAsync(
                    request.TenantId, request.Name, request.Version,
                    request.Capabilities, request.Configuration, ct);
                return Results.Created($"/api/v1/control-plane/agents/{agent.Id}", agent);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });

        cpAgents.MapPatch("/{agentId:guid}/status", async (Guid agentId,
            UpdateManagedAgentStatusRequest request, IControlPlaneService cpService, CancellationToken ct) =>
        {
            try
            {
                var agent = await cpService.UpdateAgentStatusAsync(agentId, request.Status, ct);
                return Results.Ok(agent);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });

        cpAgents.MapDelete("/{agentId:guid}", async (Guid agentId,
            IControlPlaneService cpService, CancellationToken ct) =>
        {
            try
            {
                await cpService.DeregisterAgentAsync(agentId, ct);
                return Results.Ok(new { agentId, deregistered = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });
    }

    private static void MapPolicyEndpoints(RouteGroupBuilder controlPlane)
    {
        var cpPolicies = controlPlane.MapGroup("/policies");

        cpPolicies.MapGet("", async (string? tenantId, PlatformPolicyType? policyType, bool? isEnabled,
            int? offset, int? limit, IControlPlaneService cpService, CancellationToken ct) =>
        {
            var list = await cpService.ListPoliciesAsync(tenantId, policyType, isEnabled,
                offset ?? 0, limit ?? 50, ct);
            return Results.Ok(list);
        });

        cpPolicies.MapGet("/{policyId:guid}", async (Guid policyId,
            IControlPlaneService cpService, CancellationToken ct) =>
        {
            var policy = await cpService.GetPolicyAsync(policyId, ct);
            return policy is null ? Results.NotFound() : Results.Ok(policy);
        });

        cpPolicies.MapPost("", async (CreatePlatformPolicyRequest request,
            IControlPlaneService cpService, CancellationToken ct) =>
        {
            var policy = await cpService.CreatePolicyAsync(
                request.TenantId, request.Name, request.Description, request.PolicyType,
                request.TargetResource, request.Rules, request.Priority, ct);
            return Results.Created($"/api/v1/control-plane/policies/{policy.Id}", policy);
        });

        cpPolicies.MapPut("/{policyId:guid}", async (Guid policyId,
            UpdatePlatformPolicyRequest request, IControlPlaneService cpService, CancellationToken ct) =>
        {
            try
            {
                var policy = await cpService.UpdatePolicyAsync(
                    policyId, request.IsEnabled, request.Rules, request.Priority, ct);
                return Results.Ok(policy);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });

        cpPolicies.MapDelete("/{policyId:guid}", async (Guid policyId,
            IControlPlaneService cpService, CancellationToken ct) =>
        {
            try
            {
                await cpService.DeletePolicyAsync(policyId, ct);
                return Results.Ok(new { policyId, deleted = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });
    }

    private static void MapConfigEndpoints(RouteGroupBuilder controlPlane)
    {
        var cpConfig = controlPlane.MapGroup("/config");

        cpConfig.MapGet("", async (string? tenantId, string? scope,
            int? offset, int? limit, IControlPlaneService cpService, CancellationToken ct) =>
        {
            var list = await cpService.ListConfigurationsAsync(tenantId, scope,
                offset ?? 0, limit ?? 100, ct);
            return Results.Ok(list);
        });

        cpConfig.MapGet("/{tenantId}/{scope}/{key}", async (string tenantId, string scope, string key,
            IControlPlaneService cpService, CancellationToken ct) =>
        {
            var config = await cpService.GetConfigurationAsync(tenantId, scope, key, ct);
            return config is null ? Results.NotFound() : Results.Ok(config);
        });

        cpConfig.MapPut("", async (SetConfigurationRequest request,
            IControlPlaneService cpService, CancellationToken ct) =>
        {
            var config = await cpService.SetConfigurationAsync(
                request.TenantId, request.Scope, request.Key, request.Value,
                request.Description, request.IsSecret, ct);
            return Results.Ok(config);
        });

        cpConfig.MapDelete("/{tenantId}/{scope}/{key}", async (string tenantId, string scope, string key,
            IControlPlaneService cpService, CancellationToken ct) =>
        {
            try
            {
                await cpService.DeleteConfigurationAsync(tenantId, scope, key, ct);
                return Results.Ok(new { tenantId, scope, key, deleted = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });
    }

    private static void MapObservabilityEndpoints(IEndpointRouteBuilder v1)
    {
        var cpDashboard = v1.MapGroup("/control-plane/observability")
            .RequireAuthorization("OperatorOrAdmin");

        cpDashboard.MapGet("/unified", async (IControlPlaneObservability cpObs, CancellationToken ct) =>
        {
            var dashboard = await cpObs.GetUnifiedDashboardAsync(ct);
            return Results.Ok(dashboard);
        });

        cpDashboard.MapGet("/agent-activity", async (IControlPlaneObservability cpObs, CancellationToken ct) =>
        {
            var dashboard = await cpObs.GetAgentActivityAsync(ct);
            return Results.Ok(dashboard);
        });

        cpDashboard.MapGet("/system-health", async (IControlPlaneObservability cpObs, CancellationToken ct) =>
        {
            var dashboard = await cpObs.GetSystemHealthAsync(ct);
            return Results.Ok(dashboard);
        });

        cpDashboard.MapGet("/model-usage", async (IControlPlaneObservability cpObs, CancellationToken ct) =>
        {
            var dashboard = await cpObs.GetModelUsageAsync(ct);
            return Results.Ok(dashboard);
        });

        cpDashboard.MapGet("/task-performance", async (IControlPlaneObservability cpObs, CancellationToken ct) =>
        {
            var dashboard = await cpObs.GetTaskPerformanceAsync(ct);
            return Results.Ok(dashboard);
        });
    }

    private static void MapSystemEndpoints(IEndpointRouteBuilder v1)
    {
        var sysControl = v1.MapGroup("/control-plane/system")
            .RequireAuthorization("AdminOnly");

        sysControl.MapGet("/paused", async (IControlPlaneObservability cpObs, CancellationToken ct) =>
        {
            bool paused = await cpObs.IsSystemPausedAsync(ct);
            return Results.Ok(new { isPaused = paused });
        });

        sysControl.MapPost("/pause", async (SystemPauseRequest request, IControlPlaneObservability cpObs, CancellationToken ct) =>
        {
            await cpObs.PauseSystemAsync(request.Reason, ct);
            return Results.Ok(new { paused = true, reason = request.Reason, atUtc = DateTimeOffset.UtcNow });
        });

        sysControl.MapPost("/resume", async (IControlPlaneObservability cpObs, CancellationToken ct) =>
        {
            await cpObs.ResumeSystemAsync(ct);
            return Results.Ok(new { resumed = true, atUtc = DateTimeOffset.UtcNow });
        });
    }

    private static void MapAlertEndpoints(IEndpointRouteBuilder v1)
    {
        var alerts = v1.MapGroup("/control-plane/alerts")
            .RequireAuthorization("OperatorOrAdmin");

        alerts.MapGet("/", async (IControlPlaneObservability cpObs, CancellationToken ct) =>
        {
            var activeAlerts = await cpObs.GetActiveAlertsAsync(ct);
            return Results.Ok(activeAlerts);
        });

        alerts.MapPost("/{alertId:guid}/acknowledge", async (Guid alertId, IControlPlaneObservability cpObs, CancellationToken ct) =>
        {
            await cpObs.AcknowledgeAlertAsync(alertId, ct);
            return Results.Ok(new { alertId, acknowledged = true });
        });

        alerts.MapPost("/raise", (RaiseAlertRequest request, IControlPlaneObservability cpObs) =>
        {
            cpObs.RaiseAlert(request.Severity, request.Component, request.Message);
            return Results.Ok(new { raised = true, atUtc = DateTimeOffset.UtcNow });
        }).RequireAuthorization("AdminOnly");
    }

    private static void MapStandaloneObservabilityEndpoints(IEndpointRouteBuilder v1)
    {
        var observability = v1.MapGroup("/observability")
            .RequireAuthorization("OperatorOrAdmin");

        observability.MapGet("/status", (IObservabilityService obsService) =>
        {
            var status = obsService.GetStatus();
            return Results.Ok(status);
        });

        observability.MapGet("/traces", async (Guid? agentId, int? limit, IObservabilityService obsService, CancellationToken ct) =>
        {
            var traces = await obsService.GetAgentTracesAsync(agentId, limit ?? 100, ct);
            return Results.Ok(traces);
        });

        observability.MapGet("/workflows/{workflowId:guid}/performance", async (Guid workflowId, IObservabilityService obsService, CancellationToken ct) =>
        {
            var snapshot = await obsService.GetWorkflowPerformanceAsync(workflowId, ct);
            return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
        });

        observability.MapGet("/system", async (IObservabilityService obsService, CancellationToken ct) =>
        {
            var metrics = await obsService.GetSystemMetricsAsync(ct);
            return Results.Ok(metrics);
        });

        observability.MapGet("/connectors/health", async (IObservabilityService obsService, CancellationToken ct) =>
        {
            var health = await obsService.GetConnectorHealthAsync(ct);
            return Results.Ok(health);
        });

        observability.MapGet("/agents/metrics", async (IObservabilityService obsService, CancellationToken ct) =>
        {
            var metrics = await obsService.GetAgentMetricsSummariesAsync(ct);
            return Results.Ok(metrics);
        });
    }

    private static void MapMonitoringEndpoints(IEndpointRouteBuilder v1)
    {
        var monitoring = v1.MapGroup("/monitoring")
            .RequireAuthorization("OperatorOrAdmin");

        monitoring.MapGet("/status", (IMonitoringDashboardService monService) =>
            Results.Ok(monService.GetStatus()));

        monitoring.MapGet("/dashboard", async (IMonitoringDashboardService monService, CancellationToken ct) =>
        {
            var dashboard = await monService.GetFullDashboardAsync(ct);
            return Results.Ok(dashboard);
        });

        monitoring.MapGet("/workflows", async (IMonitoringDashboardService monService, CancellationToken ct) =>
        {
            var metrics = await monService.GetWorkflowMetricsAsync(ct);
            return Results.Ok(metrics);
        });

        monitoring.MapGet("/agents", async (IMonitoringDashboardService monService, CancellationToken ct) =>
        {
            var health = await monService.GetAgentHealthAsync(ct);
            return Results.Ok(health);
        });

        monitoring.MapGet("/system", async (IMonitoringDashboardService monService, CancellationToken ct) =>
        {
            var performance = await monService.GetSystemPerformanceAsync(ct);
            return Results.Ok(performance);
        });
    }

    private static void MapClusterEndpoints(IEndpointRouteBuilder v1)
    {
        var cluster = v1.MapGroup("/cluster")
            .RequireAuthorization("OperatorOrAdmin");

        cluster.MapGet("/status", async (IClusterCoordinator clusterCoord, CancellationToken ct) =>
        {
            var status = await clusterCoord.GetClusterStatusAsync(ct);
            return Results.Ok(status);
        });

        cluster.MapGet("/dashboard", async (IClusterCoordinator clusterCoord, CancellationToken ct) =>
        {
            var dashboard = await clusterCoord.GetDashboardAsync(ct);
            return Results.Ok(dashboard);
        });

        cluster.MapGet("/nodes", async (ClusterNodeStatus? status, IClusterCoordinator clusterCoord, CancellationToken ct) =>
        {
            var nodes = await clusterCoord.ListNodesAsync(status, ct);
            return Results.Ok(nodes);
        });

        cluster.MapGet("/nodes/{nodeId:guid}", async (Guid nodeId, IClusterCoordinator clusterCoord, CancellationToken ct) =>
        {
            var node = await clusterCoord.GetNodeAsync(nodeId, ct);
            return node is null ? Results.NotFound() : Results.Ok(node);
        });

        cluster.MapPost("/nodes", async (RegisterClusterNodeRequest request, IClusterCoordinator clusterCoord, CancellationToken ct) =>
        {
            try
            {
                var capacity = new NodeCapacity(
                    request.MaxConcurrentTasks, request.MaxAgents,
                    request.CpuCores, request.MemoryBytes, request.GpuSlots);

                var node = await clusterCoord.RegisterNodeAsync(
                    request.HostName, request.Role, capacity,
                    request.Capabilities, request.Labels, ct);
                return Results.Created($"/api/v1/cluster/nodes/{node.NodeId}", node);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        });

        cluster.MapPost("/nodes/{nodeId:guid}/heartbeat", async (
            Guid nodeId, NodeHeartbeatRequest request, IClusterCoordinator clusterCoord, CancellationToken ct) =>
        {
            try
            {
                var load = new NodeLoad(
                    request.ActiveTasks, request.QueuedTasks, request.ActiveAgents,
                    request.CpuUtilizationPercent, request.MemoryUtilizationPercent,
                    request.GpuSlotsUsed, DateTimeOffset.UtcNow);

                var node = await clusterCoord.HeartbeatAsync(nodeId, load, ct);
                return Results.Ok(node);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
        });

        cluster.MapPost("/nodes/{nodeId:guid}/drain", async (
            Guid nodeId, IClusterCoordinator clusterCoord, CancellationToken ct) =>
        {
            try
            {
                await clusterCoord.DrainNodeAsync(nodeId, ct);
                return Results.Ok(new { nodeId, status = "draining" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
        });

        cluster.MapDelete("/nodes/{nodeId:guid}", async (
            Guid nodeId, IClusterCoordinator clusterCoord, CancellationToken ct) =>
        {
            try
            {
                await clusterCoord.RemoveNodeAsync(nodeId, ct);
                return Results.Ok(new { nodeId, removed = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 404);
            }
        });

        cluster.MapPost("/schedule", async (
            ClusterScheduleRequest request, IClusterCoordinator clusterCoord, CancellationToken ct) =>
        {
            var result = await clusterCoord.ScheduleOnClusterAsync(request, ct);
            return Results.Ok(result);
        });

        cluster.MapPost("/schedule/batch", async (
            IReadOnlyList<ClusterScheduleRequest> requests, IClusterCoordinator clusterCoord, CancellationToken ct) =>
        {
            var results = await clusterCoord.ScheduleBatchAsync(requests, ct);
            return Results.Ok(results);
        });

        cluster.MapGet("/distribution-plan", async (IClusterCoordinator clusterCoord, CancellationToken ct) =>
        {
            var plan = await clusterCoord.GenerateDistributionPlanAsync(ct);
            return Results.Ok(plan);
        });

        cluster.MapPost("/rebalance", async (IClusterCoordinator clusterCoord, CancellationToken ct) =>
        {
            await clusterCoord.RebalanceAsync(ct);
            return Results.Ok(new { rebalanced = true, atUtc = DateTimeOffset.UtcNow });
        }).RequireAuthorization("AdminOnly");
    }
}

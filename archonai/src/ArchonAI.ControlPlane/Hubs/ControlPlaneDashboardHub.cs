using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.ControlPlane;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ArchonAI.ControlPlane.Hubs;

/// <summary>
/// SignalR hub providing real-time dashboard updates via WebSocket.
/// Clients can subscribe to specific dashboard modules for live data.
/// </summary>
[Authorize]
public sealed class ControlPlaneDashboardHub : Hub
{
    private readonly IControlPlaneObservability _observability;

    public ControlPlaneDashboardHub(IControlPlaneObservability observability)
    {
        _observability = observability;
    }

    public override async global::System.Threading.Tasks.Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "all-dashboards");
        await base.OnConnectedAsync();
    }

    public override async global::System.Threading.Tasks.Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "all-dashboards");
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Subscribe to a specific dashboard module for targeted updates.
    /// Modules: agent-activity, system-health, model-usage, task-performance, alerts
    /// </summary>
    public async global::System.Threading.Tasks.Task SubscribeToModule(string module)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"module:{module}");
    }

    /// <summary>
    /// Unsubscribe from a specific dashboard module.
    /// </summary>
    public async global::System.Threading.Tasks.Task UnsubscribeFromModule(string module)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"module:{module}");
    }

    /// <summary>
    /// Request an immediate snapshot of all dashboards.
    /// </summary>
    public async global::System.Threading.Tasks.Task RequestFullDashboard()
    {
        var dashboard = await _observability.GetUnifiedDashboardAsync(Context.ConnectionAborted);
        await Clients.Caller.SendAsync("FullDashboardUpdate", dashboard, Context.ConnectionAborted);
    }

    /// <summary>
    /// Request an immediate snapshot of a specific module.
    /// </summary>
    public async global::System.Threading.Tasks.Task RequestModuleDashboard(string module)
    {
        object? dashboard = module.ToLowerInvariant() switch
        {
            "agent-activity" => await _observability.GetAgentActivityAsync(Context.ConnectionAborted),
            "system-health" => await _observability.GetSystemHealthAsync(Context.ConnectionAborted),
            "model-usage" => await _observability.GetModelUsageAsync(Context.ConnectionAborted),
            "task-performance" => await _observability.GetTaskPerformanceAsync(Context.ConnectionAborted),
            _ => null
        };

        if (dashboard is not null)
        {
            await Clients.Caller.SendAsync("ModuleDashboardUpdate",
                new DashboardUpdate(module, "snapshot", dashboard, DateTimeOffset.UtcNow),
                Context.ConnectionAborted);
        }
    }

    /// <summary>
    /// Request current active alerts.
    /// </summary>
    public async global::System.Threading.Tasks.Task RequestAlerts()
    {
        var alerts = await _observability.GetActiveAlertsAsync(Context.ConnectionAborted);
        await Clients.Caller.SendAsync("AlertsUpdate", alerts, Context.ConnectionAborted);
    }
}

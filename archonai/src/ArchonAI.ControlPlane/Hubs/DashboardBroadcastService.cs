using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.ControlPlane;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.ControlPlane.Hubs;

/// <summary>
/// Background service that broadcasts dashboard updates to connected
/// WebSocket clients at a configurable interval.
/// </summary>
public sealed class DashboardBroadcastService : BackgroundService
{
    private readonly IHubContext<ControlPlaneDashboardHub> _hubContext;
    private readonly IControlPlaneObservability _observability;
    private readonly ControlPlaneOptions _options;
    private readonly ILogger<DashboardBroadcastService> _logger;

    public DashboardBroadcastService(
        IHubContext<ControlPlaneDashboardHub> hubContext,
        IControlPlaneObservability observability,
        IOptions<ControlPlaneOptions> options,
        ILogger<DashboardBroadcastService> logger)
    {
        _hubContext = hubContext;
        _observability = observability;
        _options = options.Value;
        _logger = logger;
    }

    protected override async global::System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int intervalSeconds = Math.Max(5, _options.DashboardBroadcastIntervalSeconds);
        _logger.LogInformation(
            "Dashboard broadcast service started (interval: {Interval}s)", intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await global::System.Threading.Tasks.Task.Delay(
                    TimeSpan.FromSeconds(intervalSeconds), stoppingToken);

                await BroadcastUpdatesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard broadcast cycle failed");
            }
        }

        _logger.LogInformation("Dashboard broadcast service stopped");
    }

    private async global::System.Threading.Tasks.Task BroadcastUpdatesAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Broadcast agent activity to subscribers
        try
        {
            var agentActivity = await _observability.GetAgentActivityAsync(ct);
            await _hubContext.Clients.Group("module:agent-activity").SendAsync(
                "ModuleDashboardUpdate",
                new DashboardUpdate("agent-activity", "periodic", agentActivity, now), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast agent-activity update");
        }

        // Broadcast system health to subscribers
        try
        {
            var systemHealth = await _observability.GetSystemHealthAsync(ct);
            await _hubContext.Clients.Group("module:system-health").SendAsync(
                "ModuleDashboardUpdate",
                new DashboardUpdate("system-health", "periodic", systemHealth, now), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast system-health update");
        }

        // Broadcast model usage to subscribers
        try
        {
            var modelUsage = await _observability.GetModelUsageAsync(ct);
            await _hubContext.Clients.Group("module:model-usage").SendAsync(
                "ModuleDashboardUpdate",
                new DashboardUpdate("model-usage", "periodic", modelUsage, now), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast model-usage update");
        }

        // Broadcast task performance to subscribers
        try
        {
            var taskPerf = await _observability.GetTaskPerformanceAsync(ct);
            await _hubContext.Clients.Group("module:task-performance").SendAsync(
                "ModuleDashboardUpdate",
                new DashboardUpdate("task-performance", "periodic", taskPerf, now), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast task-performance update");
        }

        // Broadcast alerts to all subscribers
        try
        {
            var alerts = await _observability.GetActiveAlertsAsync(ct);
            await _hubContext.Clients.Group("module:alerts").SendAsync(
                "AlertsUpdate", alerts, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast alerts update");
        }
    }
}

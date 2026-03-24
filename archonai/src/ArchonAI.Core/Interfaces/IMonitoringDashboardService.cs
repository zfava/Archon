using ArchonAI.Core.Models.Monitoring;

namespace ArchonAI.Core.Interfaces;

public interface IMonitoringDashboardService
{
    global::System.Threading.Tasks.Task<MonitoringDashboard> GetFullDashboardAsync(
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<WorkflowMetricsDashboard> GetWorkflowMetricsAsync(
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<AgentHealthDashboard> GetAgentHealthAsync(
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<SystemPerformanceDashboard> GetSystemPerformanceAsync(
        CancellationToken ct = default);

    MonitoringDashboardServiceStatus GetStatus();
}

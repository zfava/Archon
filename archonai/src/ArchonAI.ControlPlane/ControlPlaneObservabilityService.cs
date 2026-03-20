using System.Diagnostics;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Cluster;
using ArchonAI.Core.Models.ControlPlane;
using ArchonAI.Core.Models.Models.Routing;
using Microsoft.Extensions.Logging;

namespace ArchonAI.ControlPlane;

public sealed class ControlPlaneObservabilityService : IControlPlaneObservability
{
    private readonly IControlPlaneService _controlPlane;
    private readonly IMonitoringDashboardService _monitoring;
    private readonly IModelPerformanceTracker _modelTracker;
    private readonly IPerformanceAnalyzer _performanceAnalyzer;
    private readonly IClusterCoordinator _clusterCoordinator;
    private readonly ISystemInsightEngine _insightEngine;
    private readonly IControlPlaneAlertStore _alertStore;
    private readonly ILogger<ControlPlaneObservabilityService> _logger;

    public ControlPlaneObservabilityService(
        IControlPlaneService controlPlane,
        IMonitoringDashboardService monitoring,
        IModelPerformanceTracker modelTracker,
        IPerformanceAnalyzer performanceAnalyzer,
        IClusterCoordinator clusterCoordinator,
        ISystemInsightEngine insightEngine,
        IControlPlaneAlertStore alertStore,
        ILogger<ControlPlaneObservabilityService> logger)
    {
        _controlPlane = controlPlane;
        _monitoring = monitoring;
        _modelTracker = modelTracker;
        _performanceAnalyzer = performanceAnalyzer;
        _clusterCoordinator = clusterCoordinator;
        _insightEngine = insightEngine;
        _alertStore = alertStore;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  Agent Activity
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<AgentActivityDashboard> GetAgentActivityAsync(
        CancellationToken ct = default)
    {
        var agentHealth = await _monitoring.GetAgentHealthAsync(ct);
        var managedAgents = await _controlPlane.ListManagedAgentsAsync(null, null, 0, 500, ct);

        var entries = agentHealth.Agents.Select(a => new AgentActivityEntry(
            AgentId: a.AgentId,
            Name: a.AgentName,
            Status: a.HealthStatus.ToString().ToLowerInvariant(),
            ActiveTasks: 0,
            TotalExecutions: a.ExecutionsTotal,
            FailedExecutions: a.ExecutionsFailed,
            SuccessRate: a.ExecutionsTotal > 0 ? 1.0 - a.FailureRate : 1.0,
            AverageLatencyMs: a.AverageLatencyMs,
            Capabilities: a.Capabilities,
            LastActiveAtUtc: a.LastActivityAtUtc)).ToList();

        int activeCount = managedAgents.Count(a => a.Status == ManagedAgentStatus.Active);
        int drainingCount = managedAgents.Count(a => a.Status == ManagedAgentStatus.Draining);
        long totalExec = entries.Sum(e => e.TotalExecutions);
        long totalFail = entries.Sum(e => e.FailedExecutions);

        return new AgentActivityDashboard(
            TotalRegistered: entries.Count,
            ActiveAgents: activeCount,
            IdleAgents: Math.Max(0, entries.Count - activeCount - drainingCount),
            DrainingAgents: drainingCount,
            TotalExecutions: totalExec,
            TotalFailures: totalFail,
            OverallSuccessRate: totalExec > 0 ? (double)(totalExec - totalFail) / totalExec : 1.0,
            Agents: entries,
            RecentEvents: await _alertStore.GetRecentEventsAsync(50, ct),
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    // ══════════════════════════════════════════════════════════════
    //  System Health
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<SystemHealthDashboard> GetSystemHealthAsync(
        CancellationToken ct = default)
    {
        var systemPerf = await _monitoring.GetSystemPerformanceAsync(ct);
        var clusterStatus = await _clusterCoordinator.GetClusterStatusAsync(ct);
        var insightHealth = await _insightEngine.GetHealthSummaryAsync(ct);

        var process = Process.GetCurrentProcess();
        long memoryTotal = process.WorkingSet64 > 0 ? (long)(process.WorkingSet64 / (systemPerf.CpuUsagePercent > 0 ? systemPerf.CpuUsagePercent / 100.0 : 1.0)) : 8L * 1024 * 1024 * 1024;

        var healthChecks = new List<HealthCheckResult>
        {
            new("control-plane", "healthy", "ControlPlane service is active.", 0, DateTimeOffset.UtcNow),
            new("cluster", clusterStatus.HealthStatus,
                $"Cluster: {clusterStatus.ActiveNodes} active nodes, {clusterStatus.TotalActiveTasks} tasks.",
                0, DateTimeOffset.UtcNow),
            new("monitoring", "healthy", "Monitoring dashboard service is active.", 0, DateTimeOffset.UtcNow),
            new("insight-engine", insightHealth.HealthStatus,
                $"Health score: {insightHealth.OverallHealthScore:F2}, {insightHealth.UnresolvedAnomalies} anomalies.",
                0, DateTimeOffset.UtcNow)
        };

        foreach (var (connector, status) in systemPerf.ConnectorHealth)
        {
            healthChecks.Add(new HealthCheckResult(
                $"connector:{connector}", status.IsHealthy ? "healthy" : "unhealthy",
                $"Connector '{connector}': {1.0 - status.ErrorRate:P0} success rate, {status.TotalOperations} ops.",
                0, DateTimeOffset.UtcNow));
        }

        double healthScore = (insightHealth.OverallHealthScore * 0.4 +
                              (clusterStatus.HealthStatus == "healthy" ? 1.0 : clusterStatus.HealthStatus == "degraded" ? 0.6 : 0.2) * 0.3 +
                              (1.0 - Math.Min(1.0, systemPerf.CpuUsagePercent / 100.0)) * 0.3);

        string overallStatus = healthScore >= 0.8 ? "healthy" : healthScore >= 0.5 ? "degraded" : "critical";
        var (isPaused, _) = await _alertStore.GetPauseStateAsync(ct);
        if (isPaused) overallStatus = "paused";

        return new SystemHealthDashboard(
            OverallStatus: overallStatus,
            HealthScore: Math.Round(healthScore, 3),
            Cluster: new ClusterHealthSummary(
                TotalNodes: clusterStatus.TotalNodes,
                ActiveNodes: clusterStatus.ActiveNodes,
                DrainingNodes: clusterStatus.DrainingNodes,
                OfflineNodes: clusterStatus.OfflineNodes,
                AverageCpuPercent: clusterStatus.AverageCpuUtilization,
                AverageMemoryPercent: clusterStatus.AverageMemoryUtilization,
                TotalActiveTasks: clusterStatus.TotalActiveTasks,
                ClusterStatus: clusterStatus.HealthStatus),
            Resources: new ResourceUtilization(
                CpuPercent: systemPerf.CpuUsagePercent,
                MemoryUsedBytes: systemPerf.MemoryUsedBytes,
                MemoryTotalBytes: memoryTotal,
                MemoryPercent: memoryTotal > 0 ? (double)systemPerf.MemoryUsedBytes / memoryTotal * 100 : 0,
                DiskUsedBytes: 0,
                DiskTotalBytes: 0,
                ActiveThreads: systemPerf.ThreadPool.ActiveThreads,
                PendingWorkItems: systemPerf.ThreadPool.PendingWorkItems),
            HealthChecks: healthChecks,
            ActiveAlerts: await _alertStore.GetActiveAlertsAsync(ct),
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    // ══════════════════════════════════════════════════════════════
    //  Model Usage
    // ══════════════════════════════════════════════════════════════

    public global::System.Threading.Tasks.Task<ModelUsageDashboard> GetModelUsageAsync(
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var allScores = _modelTracker.GetAllScores();

        var entries = allScores.Select(s => new ModelUsageEntry(
            Provider: s.Provider,
            Model: s.Model,
            RequestCount: s.SampleCount,
            SuccessRate: s.SuccessRate,
            AverageLatencyMs: s.AverageLatencyMs,
            P95LatencyMs: s.AverageLatencyMs * 2.0,
            TotalCost: s.AverageCostPerRequest * s.SampleCount,
            CostPerRequest: s.AverageCostPerRequest,
            AccuracyRate: s.AccuracyRate,
            CompositeScore: s.CompositeScore,
            HealthStatus: ClassifyModelHealth(s),
            LastUsedAtUtc: s.LastUpdatedUtc)).ToList();

        long totalRequests = entries.Sum(e => e.RequestCount);
        double avgLatency = entries.Count > 0 ? entries.Average(e => e.AverageLatencyMs) : 0;
        double overallSuccess = totalRequests > 0
            ? entries.Sum(e => e.RequestCount * e.SuccessRate) / totalRequests : 1.0;
        double totalCost = entries.Sum(e => e.TotalCost);

        var trends = entries.Where(e => e.RequestCount >= 10).Select(e => new ModelUsageTrend(
            Provider: e.Provider,
            Model: e.Model,
            MetricName: "latency",
            CurrentValue: e.AverageLatencyMs,
            PreviousValue: e.AverageLatencyMs,
            ChangePercent: 0,
            TrendDirection: "stable")).ToList();

        IReadOnlyList<ModelUsageTrend> trendResult = trends;

        var result = new ModelUsageDashboard(
            TotalModels: entries.Count,
            TotalRequests: totalRequests,
            OverallSuccessRate: overallSuccess,
            AverageLatencyMs: avgLatency,
            TotalCost: totalCost,
            Models: entries.OrderByDescending(e => e.RequestCount).ToList(),
            Trends: trendResult,
            GeneratedAtUtc: DateTimeOffset.UtcNow);

        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    // ══════════════════════════════════════════════════════════════
    //  Task Performance
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<TaskPerformanceDashboard> GetTaskPerformanceAsync(
        CancellationToken ct = default)
    {
        var report = await _performanceAnalyzer.AnalyzeAsync(ct);

        var byTaskType = report.TaskCompletion.Select(t => new TaskTypePerformance(
            TaskType: t.TaskType,
            Total: t.TotalTasks,
            Completed: t.Completed,
            Failed: t.Failed,
            CompletionRate: t.CompletionRate,
            AverageExecutionTimeMs: t.AverageExecutionTimeMs,
            AverageCost: (double)t.AverageCost,
            P95ExecutionTimeMs: t.AverageExecutionTimeMs * 2.5)).ToList();

        long totalTasks = byTaskType.Sum(t => t.Total);
        long completedTasks = byTaskType.Sum(t => t.Completed);
        long failedTasks = byTaskType.Sum(t => t.Failed);
        double avgExecTime = byTaskType.Count > 0 ? byTaskType.Average(t => t.AverageExecutionTimeMs) : 0;
        double avgCost = byTaskType.Count > 0 ? byTaskType.Average(t => t.AverageCost) : 0;

        var trends = byTaskType.Where(t => t.Total >= 5).Select(t => new TaskPerformanceTrend(
            TaskType: t.TaskType,
            MetricName: "completion-rate",
            CurrentValue: t.CompletionRate,
            PreviousValue: t.CompletionRate,
            ChangePercent: 0,
            TrendDirection: "stable")).ToList();

        return new TaskPerformanceDashboard(
            TotalTasks: totalTasks,
            CompletedTasks: completedTasks,
            FailedTasks: failedTasks,
            OverallCompletionRate: totalTasks > 0 ? (double)completedTasks / totalTasks : 1.0,
            AverageExecutionTimeMs: avgExecTime,
            AverageCost: avgCost,
            ByTaskType: byTaskType,
            Trends: trends,
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    // ══════════════════════════════════════════════════════════════
    //  Unified Dashboard
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task<UnifiedControlPlaneDashboard> GetUnifiedDashboardAsync(
        CancellationToken ct = default)
    {
        var agentActivity = await GetAgentActivityAsync(ct);
        var systemHealth = await GetSystemHealthAsync(ct);
        var modelUsage = await GetModelUsageAsync(ct);
        var taskPerformance = await GetTaskPerformanceAsync(ct);
        var cpStatus = _controlPlane.GetStatus();

        return new UnifiedControlPlaneDashboard(
            AgentActivity: agentActivity,
            SystemHealth: systemHealth,
            ModelUsage: modelUsage,
            TaskPerformance: taskPerformance,
            ControlPlane: cpStatus,
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    // ══════════════════════════════════════════════════════════════
    //  System Control
    // ══════════════════════════════════════════════════════════════

    public async global::System.Threading.Tasks.Task PauseSystemAsync(string reason, CancellationToken ct = default)
    {
        await _alertStore.SetPauseStateAsync(true, reason, ct);
        _logger.LogWarning("System PAUSED: {Reason}", reason);

        RaiseAlert("warning", "system", $"System paused: {reason}");
    }

    public async global::System.Threading.Tasks.Task ResumeSystemAsync(CancellationToken ct = default)
    {
        var (_, previousReason) = await _alertStore.GetPauseStateAsync(ct);
        await _alertStore.SetPauseStateAsync(false, null, ct);
        _logger.LogInformation("System RESUMED from pause (was: {Reason})", previousReason);
    }

    public async global::System.Threading.Tasks.Task<bool> IsSystemPausedAsync(CancellationToken ct = default)
    {
        var (isPaused, _) = await _alertStore.GetPauseStateAsync(ct);
        return isPaused;
    }

    // ══════════════════════════════════════════════════════════════
    //  Alert Management
    // ══════════════════════════════════════════════════════════════

    public global::System.Threading.Tasks.Task<IReadOnlyList<SystemAlert>> GetActiveAlertsAsync(
        CancellationToken ct = default)
    {
        return _alertStore.GetActiveAlertsAsync(ct);
    }

    public async global::System.Threading.Tasks.Task AcknowledgeAlertAsync(Guid alertId, CancellationToken ct = default)
    {
        await _alertStore.AcknowledgeAlertAsync(alertId, ct);
    }

    public void RaiseAlert(string severity, string component, string message)
    {
        var alert = new SystemAlert(
            AlertId: Guid.NewGuid(),
            Severity: severity,
            Component: component,
            Message: message,
            IsAcknowledged: false,
            RaisedAtUtc: DateTimeOffset.UtcNow);

        // Fire-and-forget: alert store handles persistence and eviction
        _ = _alertStore.UpsertAlertAsync(alert);
        _ = _alertStore.EvictStaleAlertsAsync();
    }

    private static string ClassifyModelHealth(ModelPerformanceScore score)
    {
        if (score.SuccessRate < 0.8) return "critical";
        if (score.SuccessRate < 0.9 || score.AverageLatencyMs > 10000) return "degraded";
        if (score.AverageLatencyMs > 5000) return "slow";
        return "healthy";
    }
}

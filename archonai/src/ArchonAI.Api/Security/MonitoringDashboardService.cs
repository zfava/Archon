using System.Diagnostics;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Monitoring;
using OTel = ArchonAI.Common.Observability.Telemetry;
using ArchonAI.Core.Models.Observability;
using ArchonAI.Core.Models.Workflow;
using ArchonAI.Registry;

namespace ArchonAI.Api.Security;

public sealed class MonitoringDashboardService : IMonitoringDashboardService
{
    private readonly IObservabilityService _observability;
    private readonly IWorkflowDesignService _workflowDesign;
    private readonly IAgentCapabilityRegistry _capabilityRegistry;
    private readonly ILogger<MonitoringDashboardService> _logger;

    private long _dashboardsGenerated;
    private long _workflowMetricsQueries;
    private long _agentHealthQueries;
    private long _systemPerformanceQueries;

    public MonitoringDashboardService(
        IObservabilityService observability,
        IWorkflowDesignService workflowDesign,
        IAgentCapabilityRegistry capabilityRegistry,
        ILogger<MonitoringDashboardService> logger)
    {
        _observability = observability;
        _workflowDesign = workflowDesign;
        _capabilityRegistry = capabilityRegistry;
        _logger = logger;
    }

    public async global::System.Threading.Tasks.Task<MonitoringDashboard> GetFullDashboardAsync(
        CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("Monitoring.FullDashboard");
        Interlocked.Increment(ref _dashboardsGenerated);
        OTel.MonitoringDashboardsGenerated.Add(1);

        var workflowTask = GetWorkflowMetricsCoreAsync(ct);
        var agentTask = GetAgentHealthCoreAsync(ct);
        var systemTask = GetSystemPerformanceCoreAsync(ct);

        await global::System.Threading.Tasks.Task.WhenAll(workflowTask, agentTask, systemTask);

        var dashboard = new MonitoringDashboard(
            WorkflowMetrics: await workflowTask,
            AgentHealth: await agentTask,
            SystemPerformance: await systemTask,
            GeneratedAtUtc: DateTimeOffset.UtcNow);

        _logger.LogInformation("Full monitoring dashboard generated");
        return dashboard;
    }

    public async global::System.Threading.Tasks.Task<WorkflowMetricsDashboard> GetWorkflowMetricsAsync(
        CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("Monitoring.WorkflowMetrics");
        Interlocked.Increment(ref _workflowMetricsQueries);
        OTel.MonitoringWorkflowMetricsQueries.Add(1);
        return await GetWorkflowMetricsCoreAsync(ct);
    }

    public async global::System.Threading.Tasks.Task<AgentHealthDashboard> GetAgentHealthAsync(
        CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("Monitoring.AgentHealth");
        Interlocked.Increment(ref _agentHealthQueries);
        OTel.MonitoringAgentHealthQueries.Add(1);
        return await GetAgentHealthCoreAsync(ct);
    }

    public async global::System.Threading.Tasks.Task<SystemPerformanceDashboard> GetSystemPerformanceAsync(
        CancellationToken ct = default)
    {
        using var activity = OTel.ActivitySource.StartActivity("Monitoring.SystemPerformance");
        Interlocked.Increment(ref _systemPerformanceQueries);
        OTel.MonitoringSystemPerformanceQueries.Add(1);
        return await GetSystemPerformanceCoreAsync(ct);
    }

    public MonitoringDashboardServiceStatus GetStatus()
    {
        return new MonitoringDashboardServiceStatus(
            IsActive: true,
            DashboardsGenerated: Interlocked.Read(ref _dashboardsGenerated),
            WorkflowMetricsQueries: Interlocked.Read(ref _workflowMetricsQueries),
            AgentHealthQueries: Interlocked.Read(ref _agentHealthQueries),
            SystemPerformanceQueries: Interlocked.Read(ref _systemPerformanceQueries),
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    // ── Workflow metrics core ──────────────────────────────────────

    private async global::System.Threading.Tasks.Task<WorkflowMetricsDashboard> GetWorkflowMetricsCoreAsync(
        CancellationToken ct)
    {
        var designStatus = _workflowDesign.GetStatus();
        var allWorkflows = await _workflowDesign.ListWorkflowsAsync(limit: 1000, ct: ct);

        // Status breakdown
        var statusGroups = allWorkflows
            .GroupBy(w => w.Status.ToString())
            .Select(g => new WorkflowStatusBreakdown(
                Status: g.Key,
                Count: g.Count(),
                Percentage: allWorkflows.Count > 0
                    ? Math.Round((double)g.Count() / allWorkflows.Count * 100.0, 2)
                    : 0.0))
            .OrderByDescending(b => b.Count)
            .ToList();

        // Recent executions
        var recentExecutions = allWorkflows
            .Where(w => w.Status is WorkflowDesignStatus.Completed or WorkflowDesignStatus.Failed
                or WorkflowDesignStatus.Executing)
            .OrderByDescending(w => w.ValidatedAtUtc ?? w.CreatedAtUtc)
            .Take(20)
            .Select(w => new RecentWorkflowExecution(
                WorkflowId: w.Id,
                WorkflowName: w.Name,
                Status: w.Status.ToString(),
                TotalSteps: w.Steps.Count,
                CompletedSteps: w.Status == WorkflowDesignStatus.Completed ? w.Steps.Count : 0,
                FailedSteps: w.Status == WorkflowDesignStatus.Failed ? w.Steps.Count : 0,
                DurationMs: 0.0,
                ExecutedAtUtc: w.ValidatedAtUtc ?? w.CreatedAtUtc))
            .ToList();

        long totalExecuted = designStatus.ExecutedWorkflows;
        long totalFailed = designStatus.FailedExecutions;
        double successRate = totalExecuted > 0
            ? Math.Round((double)(totalExecuted - totalFailed) / totalExecuted * 100.0, 2)
            : 100.0;

        return new WorkflowMetricsDashboard(
            TotalWorkflowsCreated: designStatus.TotalWorkflows,
            TotalWorkflowsValidated: designStatus.ValidatedWorkflows,
            TotalWorkflowsExecuted: designStatus.ExecutedWorkflows,
            TotalWorkflowsFailed: designStatus.FailedExecutions,
            ExecutionSuccessRate: successRate,
            AverageExecutionDurationMs: 0.0,
            StatusBreakdown: statusGroups,
            RecentExecutions: recentExecutions,
            CapturedAtUtc: DateTimeOffset.UtcNow);
    }

    // ── Agent health core ──────────────────────────────────────────

    private async global::System.Threading.Tasks.Task<AgentHealthDashboard> GetAgentHealthCoreAsync(
        CancellationToken ct)
    {
        var registeredAgents = await _capabilityRegistry.GetAllAsync(ct);
        var agentMetrics = await _observability.GetAgentMetricsSummariesAsync(ct);

        var agentDetails = new List<AgentHealthDetail>();

        foreach (var agent in registeredAgents)
        {
            agentMetrics.TryGetValue(agent.AgentName, out var metrics);

            long execTotal = metrics?.ExecutionsTotal ?? agent.Executions;
            long execFailed = metrics?.ExecutionsFailed ?? 0;
            double failureRate = execTotal > 0
                ? Math.Round((double)execFailed / execTotal * 100.0, 2)
                : 0.0;

            double avgLatency = metrics?.AverageDurationMs ?? agent.AverageLatencyMs;

            var healthStatus = DetermineAgentHealth(failureRate, avgLatency, execTotal);

            agentDetails.Add(new AgentHealthDetail(
                AgentId: agent.AgentId,
                AgentName: agent.AgentName,
                Version: agent.Version,
                IsEnabled: true,
                HealthStatus: healthStatus,
                ExecutionsTotal: execTotal,
                ExecutionsFailed: execFailed,
                FailureRate: failureRate,
                AverageLatencyMs: Math.Round(avgLatency, 2),
                Capabilities: agent.Capabilities,
                LastActivityAtUtc: metrics?.LastExecutionAtUtc ?? agent.UpdatedAtUtc));
        }

        int healthy = agentDetails.Count(a => a.HealthStatus == AgentHealthStatus.Healthy);
        int degraded = agentDetails.Count(a => a.HealthStatus == AgentHealthStatus.Degraded);
        int unhealthy = agentDetails.Count(a => a.HealthStatus == AgentHealthStatus.Unhealthy);

        double overallScore = agentDetails.Count > 0
            ? Math.Round((double)healthy / agentDetails.Count * 100.0, 2)
            : 100.0;

        return new AgentHealthDashboard(
            TotalAgents: agentDetails.Count,
            HealthyAgents: healthy,
            DegradedAgents: degraded,
            UnhealthyAgents: unhealthy,
            OverallHealthScore: overallScore,
            Agents: agentDetails.OrderBy(a => a.HealthStatus).ThenBy(a => a.AgentName).ToList(),
            CapturedAtUtc: DateTimeOffset.UtcNow);
    }

    private static AgentHealthStatus DetermineAgentHealth(double failureRate, double avgLatencyMs, long executionCount)
    {
        if (executionCount == 0)
            return AgentHealthStatus.Unknown;

        if (failureRate > 25.0 || avgLatencyMs > 30_000)
            return AgentHealthStatus.Unhealthy;

        if (failureRate > 10.0 || avgLatencyMs > 10_000)
            return AgentHealthStatus.Degraded;

        return AgentHealthStatus.Healthy;
    }

    // ── System performance core ────────────────────────────────────

    private async global::System.Threading.Tasks.Task<SystemPerformanceDashboard> GetSystemPerformanceCoreAsync(
        CancellationToken ct)
    {
        var systemMetrics = await _observability.GetSystemMetricsAsync(ct);
        var connectorHealth = await _observability.GetConnectorHealthAsync(ct);

        var process = Process.GetCurrentProcess();

        ThreadPool.GetAvailableThreads(out int workerAvailable, out int completionAvailable);
        ThreadPool.GetMinThreads(out int minWorker, out _);
        ThreadPool.GetMaxThreads(out int maxWorker, out _);

        var gcMetrics = new GarbageCollectionMetrics(
            Gen0Collections: systemMetrics.GcGen0Collections,
            Gen1Collections: systemMetrics.GcGen1Collections,
            Gen2Collections: systemMetrics.GcGen2Collections,
            TotalPauseDurationMs: (long)GC.GetTotalPauseDuration().TotalMilliseconds,
            FragmentationPercent: GetFragmentationPercent());

        var threadPoolMetrics = new ThreadPoolMetrics(
            ActiveThreads: systemMetrics.ThreadPoolThreadCount,
            AvailableWorkerThreads: workerAvailable,
            AvailableCompletionPortThreads: completionAvailable,
            PendingWorkItems: systemMetrics.ThreadPoolPendingWorkItems,
            MinWorkerThreads: minWorker,
            MaxWorkerThreads: maxWorker);

        var taskMetrics = new TaskExecutionMetrics(
            TotalExecuted: systemMetrics.TotalTasksExecuted,
            TotalFailed: systemMetrics.TotalTasksFailed,
            TotalQueued: 0,
            SuccessRate: systemMetrics.TaskSuccessRate,
            AverageDurationMs: 0.0,
            EventsPublished: 0,
            EventsDeadLettered: 0);

        var startTime = process.StartTime.ToUniversalTime();
        var uptime = new ProcessUptimeInfo(
            StartedAtUtc: new DateTimeOffset(startTime),
            UptimeHours: Math.Round((DateTimeOffset.UtcNow - startTime).TotalHours, 2),
            TotalRequestsHandled: 0);

        return new SystemPerformanceDashboard(
            CpuUsagePercent: systemMetrics.CpuUsagePercent,
            MemoryUsedBytes: systemMetrics.MemoryUsedBytes,
            MemoryAllocatedBytes: GC.GetTotalMemory(forceFullCollection: false),
            GcMetrics: gcMetrics,
            ThreadPool: threadPoolMetrics,
            TaskExecution: taskMetrics,
            ConnectorHealth: connectorHealth,
            Uptime: uptime,
            CapturedAtUtc: DateTimeOffset.UtcNow);
    }

    private static double GetFragmentationPercent()
    {
        var gcInfo = GC.GetGCMemoryInfo();
        long totalHeap = gcInfo.HeapSizeBytes;
        long fragmented = gcInfo.FragmentedBytes;
        return totalHeap > 0
            ? Math.Round((double)fragmented / totalHeap * 100.0, 2)
            : 0.0;
    }
}

using ArchonAI.Core.Models.Observability;

namespace ArchonAI.Core.Models.Monitoring;

public sealed record MonitoringDashboard(
    WorkflowMetricsDashboard WorkflowMetrics,
    AgentHealthDashboard AgentHealth,
    SystemPerformanceDashboard SystemPerformance,
    DateTimeOffset GeneratedAtUtc);

// ── Workflow metrics ───────────────────────────────────────────────

public sealed record WorkflowMetricsDashboard(
    long TotalWorkflowsCreated,
    long TotalWorkflowsValidated,
    long TotalWorkflowsExecuted,
    long TotalWorkflowsFailed,
    double ExecutionSuccessRate,
    double AverageExecutionDurationMs,
    IReadOnlyList<WorkflowStatusBreakdown> StatusBreakdown,
    IReadOnlyList<RecentWorkflowExecution> RecentExecutions,
    DateTimeOffset CapturedAtUtc);

public sealed record WorkflowStatusBreakdown(
    string Status,
    int Count,
    double Percentage);

public sealed record RecentWorkflowExecution(
    Guid WorkflowId,
    string WorkflowName,
    string Status,
    int TotalSteps,
    int CompletedSteps,
    int FailedSteps,
    double DurationMs,
    DateTimeOffset ExecutedAtUtc);

// ── Agent health ───────────────────────────────────────────────────

public sealed record AgentHealthDashboard(
    int TotalAgents,
    int HealthyAgents,
    int DegradedAgents,
    int UnhealthyAgents,
    double OverallHealthScore,
    IReadOnlyList<AgentHealthDetail> Agents,
    DateTimeOffset CapturedAtUtc);

public sealed record AgentHealthDetail(
    Guid AgentId,
    string AgentName,
    string Version,
    bool IsEnabled,
    AgentHealthStatus HealthStatus,
    long ExecutionsTotal,
    long ExecutionsFailed,
    double FailureRate,
    double AverageLatencyMs,
    IReadOnlyList<string> Capabilities,
    DateTimeOffset LastActivityAtUtc);

public enum AgentHealthStatus
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown
}

// ── System performance ─────────────────────────────────────────────

public sealed record SystemPerformanceDashboard(
    double CpuUsagePercent,
    long MemoryUsedBytes,
    long MemoryAllocatedBytes,
    GarbageCollectionMetrics GcMetrics,
    ThreadPoolMetrics ThreadPool,
    TaskExecutionMetrics TaskExecution,
    IReadOnlyDictionary<string, ConnectorHealthStatus> ConnectorHealth,
    ProcessUptimeInfo Uptime,
    DateTimeOffset CapturedAtUtc);

public sealed record GarbageCollectionMetrics(
    long Gen0Collections,
    long Gen1Collections,
    long Gen2Collections,
    long TotalPauseDurationMs,
    double FragmentationPercent);

public sealed record ThreadPoolMetrics(
    int ActiveThreads,
    int AvailableWorkerThreads,
    int AvailableCompletionPortThreads,
    long PendingWorkItems,
    int MinWorkerThreads,
    int MaxWorkerThreads);

public sealed record TaskExecutionMetrics(
    long TotalExecuted,
    long TotalFailed,
    long TotalQueued,
    double SuccessRate,
    double AverageDurationMs,
    long EventsPublished,
    long EventsDeadLettered);

public sealed record ProcessUptimeInfo(
    DateTimeOffset StartedAtUtc,
    double UptimeHours,
    long TotalRequestsHandled);

// ── Service status ─────────────────────────────────────────────────

public sealed record MonitoringDashboardServiceStatus(
    bool IsActive,
    long DashboardsGenerated,
    long WorkflowMetricsQueries,
    long AgentHealthQueries,
    long SystemPerformanceQueries,
    DateTimeOffset StatusAsOfUtc);

namespace ArchonAI.Core.Models.ControlPlane;

// ── Agent Activity Dashboard ──────────────────────────────────────

public sealed record AgentActivityDashboard(
    int TotalRegistered,
    int ActiveAgents,
    int IdleAgents,
    int DrainingAgents,
    long TotalExecutions,
    long TotalFailures,
    double OverallSuccessRate,
    IReadOnlyList<AgentActivityEntry> Agents,
    IReadOnlyList<AgentActivityEvent> RecentEvents,
    DateTimeOffset GeneratedAtUtc);

public sealed record AgentActivityEntry(
    Guid AgentId,
    string Name,
    string Status,
    int ActiveTasks,
    long TotalExecutions,
    long FailedExecutions,
    double SuccessRate,
    double AverageLatencyMs,
    IReadOnlyList<string> Capabilities,
    DateTimeOffset LastActiveAtUtc);

public sealed record AgentActivityEvent(
    Guid AgentId,
    string AgentName,
    string EventType,
    string Description,
    DateTimeOffset OccurredAtUtc);

// ── System Health Dashboard ───────────────────────────────────────

public sealed record SystemHealthDashboard(
    string OverallStatus,
    double HealthScore,
    ClusterHealthSummary Cluster,
    ResourceUtilization Resources,
    IReadOnlyList<HealthCheckResult> HealthChecks,
    IReadOnlyList<SystemAlert> ActiveAlerts,
    DateTimeOffset GeneratedAtUtc);

public sealed record ClusterHealthSummary(
    int TotalNodes,
    int ActiveNodes,
    int DrainingNodes,
    int OfflineNodes,
    double AverageCpuPercent,
    double AverageMemoryPercent,
    int TotalActiveTasks,
    string ClusterStatus);

public sealed record ResourceUtilization(
    double CpuPercent,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    double MemoryPercent,
    long DiskUsedBytes,
    long DiskTotalBytes,
    int ActiveThreads,
    long PendingWorkItems);

public sealed record HealthCheckResult(
    string Component,
    string Status,
    string Description,
    double ResponseTimeMs,
    DateTimeOffset CheckedAtUtc);

public sealed record SystemAlert(
    Guid AlertId,
    string Severity,
    string Component,
    string Message,
    bool IsAcknowledged,
    DateTimeOffset RaisedAtUtc);

// ── Model Usage Dashboard ─────────────────────────────────────────

public sealed record ModelUsageDashboard(
    int TotalModels,
    long TotalRequests,
    double OverallSuccessRate,
    double AverageLatencyMs,
    double TotalCost,
    IReadOnlyList<ModelUsageEntry> Models,
    IReadOnlyList<ModelUsageTrend> Trends,
    DateTimeOffset GeneratedAtUtc);

public sealed record ModelUsageEntry(
    string Provider,
    string Model,
    long RequestCount,
    double SuccessRate,
    double AverageLatencyMs,
    double P95LatencyMs,
    double TotalCost,
    double CostPerRequest,
    double AccuracyRate,
    double CompositeScore,
    string HealthStatus,
    DateTimeOffset LastUsedAtUtc);

public sealed record ModelUsageTrend(
    string Provider,
    string Model,
    string MetricName,
    double CurrentValue,
    double PreviousValue,
    double ChangePercent,
    string TrendDirection);

// ── Task Performance Dashboard ────────────────────────────────────

public sealed record TaskPerformanceDashboard(
    long TotalTasks,
    long CompletedTasks,
    long FailedTasks,
    double OverallCompletionRate,
    double AverageExecutionTimeMs,
    double AverageCost,
    IReadOnlyList<TaskTypePerformance> ByTaskType,
    IReadOnlyList<TaskPerformanceTrend> Trends,
    DateTimeOffset GeneratedAtUtc);

public sealed record TaskTypePerformance(
    string TaskType,
    long Total,
    long Completed,
    long Failed,
    double CompletionRate,
    double AverageExecutionTimeMs,
    double AverageCost,
    double P95ExecutionTimeMs);

public sealed record TaskPerformanceTrend(
    string TaskType,
    string MetricName,
    double CurrentValue,
    double PreviousValue,
    double ChangePercent,
    string TrendDirection);

// ── Unified ControlPlane Dashboard ─────────────────────────────────

public sealed record UnifiedControlPlaneDashboard(
    AgentActivityDashboard AgentActivity,
    SystemHealthDashboard SystemHealth,
    ModelUsageDashboard ModelUsage,
    TaskPerformanceDashboard TaskPerformance,
    ControlPlaneStatus ControlPlane,
    DateTimeOffset GeneratedAtUtc);

// ── Real-time update payloads ──────────────────────────────────────

public sealed record DashboardUpdate(
    string Module,
    string UpdateType,
    object Payload,
    DateTimeOffset TimestampUtc);

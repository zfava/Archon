namespace ArchonAI.Core.Models.Observability;

public sealed record AgentExecutionTrace(
    Guid TraceId,
    Guid AgentId,
    string AgentName,
    Guid TaskId,
    string Capability,
    bool IsSuccess,
    double DurationMs,
    IReadOnlyDictionary<string, string> Tags,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed record WorkflowPerformanceSnapshot(
    Guid WorkflowId,
    string WorkflowName,
    string Status,
    int TotalSteps,
    int CompletedSteps,
    int FailedSteps,
    double TotalDurationMs,
    double AverageStepDurationMs,
    IReadOnlyList<StepPerformance> StepDetails,
    DateTimeOffset CapturedAtUtc);

public sealed record StepPerformance(
    int StepOrder,
    string StepName,
    string AgentName,
    bool IsSuccess,
    double DurationMs,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record SystemMetricsSnapshot(
    double CpuUsagePercent,
    long MemoryUsedBytes,
    long GcGen0Collections,
    long GcGen1Collections,
    long GcGen2Collections,
    int ThreadPoolThreadCount,
    int ThreadPoolPendingWorkItems,
    long TotalTasksExecuted,
    long TotalTasksFailed,
    double TaskSuccessRate,
    IReadOnlyDictionary<string, ConnectorHealthStatus> ConnectorHealth,
    IReadOnlyDictionary<string, AgentMetricsSummary> AgentMetrics,
    DateTimeOffset CapturedAtUtc);

public sealed record ConnectorHealthStatus(
    string Name,
    bool IsHealthy,
    long TotalOperations,
    long TotalErrors,
    double ErrorRate,
    DateTimeOffset LastCheckAtUtc);

public sealed record AgentMetricsSummary(
    Guid AgentId,
    string AgentName,
    long ExecutionsTotal,
    long ExecutionsFailed,
    double AverageDurationMs,
    DateTimeOffset LastExecutionAtUtc);

public sealed record ObservabilityServiceStatus(
    bool IsActive,
    long TracesCollected,
    long MetricsSnapshots,
    long AlertsGenerated,
    DateTimeOffset StatusAsOfUtc);

namespace ArchonAI.Core.Models.Telemetry;

public sealed record SystemInsightDashboard(
    IReadOnlyList<BottleneckReport> Bottlenecks,
    IReadOnlyList<AgentLoadSnapshot> AgentLoad,
    IReadOnlyList<ModelLatencySnapshot> ModelLatency,
    IReadOnlyList<DetectedAnomaly> Anomalies,
    SystemHealthSummary HealthSummary,
    DateTimeOffset GeneratedAtUtc);

public sealed record BottleneckReport(
    string Component,
    string Category,
    string Description,
    double SeverityScore,
    IReadOnlyDictionary<string, string> Metrics,
    DateTimeOffset DetectedAtUtc);

public sealed record AgentLoadSnapshot(
    Guid AgentId,
    string AgentName,
    int ActiveTasks,
    int QueuedTasks,
    double AverageExecutionTimeMs,
    double CpuUtilizationPercent,
    double MemoryUtilizationPercent,
    string LoadLevel,
    DateTimeOffset SnapshotAtUtc);

public sealed record ModelLatencySnapshot(
    string Provider,
    string Model,
    double AverageLatencyMs,
    double P50LatencyMs,
    double P95LatencyMs,
    double P99LatencyMs,
    int RequestCount,
    double ErrorRate,
    string HealthStatus,
    DateTimeOffset SnapshotAtUtc);

public sealed record DetectedAnomaly(
    Guid Id,
    string Category,
    string Component,
    string Description,
    double AnomalyScore,
    double ExpectedValue,
    double ActualValue,
    string Severity,
    bool IsResolved,
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset? ResolvedAtUtc);

public sealed record SystemHealthSummary(
    double OverallHealthScore,
    int TotalActiveAgents,
    int TotalActiveModels,
    int OpenBottlenecks,
    int UnresolvedAnomalies,
    double AverageAgentLoad,
    double AverageModelLatencyMs,
    string HealthStatus,
    DateTimeOffset EvaluatedAtUtc);

public sealed record InsightTrend(
    string MetricName,
    string Component,
    IReadOnlyList<InsightDataPoint> DataPoints,
    double TrendDirection,
    string TrendLabel,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc);

public sealed record InsightDataPoint(
    double Value,
    DateTimeOffset TimestampUtc);

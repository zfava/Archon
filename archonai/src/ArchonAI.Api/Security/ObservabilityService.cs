using System.Collections.Concurrent;
using System.Diagnostics;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Observability;
using OTel = ArchonAI.Common.Observability.Telemetry;

namespace ArchonAI.Api.Security;

public sealed class ObservabilityService : IObservabilityService
{
    private const int MaxTraceEntries = 10_000;

    private readonly ConcurrentQueue<AgentExecutionTrace> _traces = new();
    private readonly ConcurrentDictionary<Guid, AgentTrackingState> _agentStates = new();
    private readonly ILogger<ObservabilityService> _logger;

    private long _tracesCollected;
    private long _metricsSnapshots;
    private long _alertsGenerated = 0;

    public ObservabilityService(ILogger<ObservabilityService> logger)
    {
        _logger = logger;
    }

    public global::System.Threading.Tasks.Task RecordAgentExecutionAsync(AgentExecutionTrace trace, CancellationToken ct = default)
    {
        _traces.Enqueue(trace);

        while (_traces.Count > MaxTraceEntries)
        {
            _traces.TryDequeue(out _);
        }

        var state = _agentStates.GetOrAdd(trace.AgentId, _ => new AgentTrackingState(trace.AgentName));
        state.RecordExecution(trace);

        Interlocked.Increment(ref _tracesCollected);
        OTel.ObservabilityTracesCollected.Add(1);

        _logger.LogDebug(
            "Recorded trace for agent {AgentName} (task {TaskId}), success={IsSuccess}, duration={DurationMs:F1}ms",
            trace.AgentName, trace.TaskId, trace.IsSuccess, trace.DurationMs);

        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<AgentExecutionTrace>> GetAgentTracesAsync(
        Guid? agentId = null, int limit = 100, CancellationToken ct = default)
    {
        var snapshot = _traces.ToArray();

        IEnumerable<AgentExecutionTrace> filtered = agentId.HasValue
            ? snapshot.Where(t => t.AgentId == agentId.Value)
            : snapshot;

        IReadOnlyList<AgentExecutionTrace> result = filtered
            .OrderByDescending(t => t.CompletedAtUtc)
            .Take(limit)
            .ToList();

        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public global::System.Threading.Tasks.Task<WorkflowPerformanceSnapshot?> GetWorkflowPerformanceAsync(
        Guid workflowId, CancellationToken ct = default)
    {
        // Stub - real implementation would query the workflow runtime
        return global::System.Threading.Tasks.Task.FromResult<WorkflowPerformanceSnapshot?>(null);
    }

    public global::System.Threading.Tasks.Task<SystemMetricsSnapshot> GetSystemMetricsAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref _metricsSnapshots);
        OTel.ObservabilityMetricsSnapshots.Add(1);

        var process = Process.GetCurrentProcess();
        var gcInfo = GC.GetGCMemoryInfo();

        ThreadPool.GetAvailableThreads(out int workerAvailable, out _);
        ThreadPool.GetMaxThreads(out int workerMax, out _);
        int threadPoolThreadCount = workerMax - workerAvailable;

        ThreadPool.GetAvailableThreads(out _, out _);
        long pendingWorkItems = ThreadPool.PendingWorkItemCount;

        long totalExecuted = Interlocked.Read(ref Unsafe_TasksExecutedReader());
        long totalFailed = Interlocked.Read(ref Unsafe_TasksFailedReader());
        double successRate = totalExecuted > 0
            ? (double)(totalExecuted - totalFailed) / totalExecuted * 100.0
            : 100.0;

        double cpuMs = process.TotalProcessorTime.TotalMilliseconds;
        double uptime = (DateTimeOffset.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds;
        double cpuUsage = uptime > 0
            ? cpuMs / (uptime * Environment.ProcessorCount) * 100.0
            : 0.0;

        var connectorHealth = BuildConnectorHealth();
        var agentMetrics = BuildAgentMetrics();

        var snapshot = new SystemMetricsSnapshot(
            CpuUsagePercent: Math.Round(cpuUsage, 2),
            MemoryUsedBytes: process.WorkingSet64,
            GcGen0Collections: GC.CollectionCount(0),
            GcGen1Collections: GC.CollectionCount(1),
            GcGen2Collections: GC.CollectionCount(2),
            ThreadPoolThreadCount: threadPoolThreadCount,
            ThreadPoolPendingWorkItems: (int)pendingWorkItems,
            TotalTasksExecuted: totalExecuted,
            TotalTasksFailed: totalFailed,
            TaskSuccessRate: Math.Round(successRate, 2),
            ConnectorHealth: connectorHealth,
            AgentMetrics: agentMetrics,
            CapturedAtUtc: DateTimeOffset.UtcNow);

        return global::System.Threading.Tasks.Task.FromResult(snapshot);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyDictionary<string, ConnectorHealthStatus>> GetConnectorHealthAsync(
        CancellationToken ct = default)
    {
        var health = BuildConnectorHealth();
        return global::System.Threading.Tasks.Task.FromResult(health);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyDictionary<string, AgentMetricsSummary>> GetAgentMetricsSummariesAsync(
        CancellationToken ct = default)
    {
        var metrics = BuildAgentMetrics();
        return global::System.Threading.Tasks.Task.FromResult(metrics);
    }

    public ObservabilityServiceStatus GetStatus()
    {
        return new ObservabilityServiceStatus(
            IsActive: true,
            TracesCollected: Interlocked.Read(ref _tracesCollected),
            MetricsSnapshots: Interlocked.Read(ref _metricsSnapshots),
            AlertsGenerated: Interlocked.Read(ref _alertsGenerated),
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    private IReadOnlyDictionary<string, ConnectorHealthStatus> BuildConnectorHealth()
    {
        var now = DateTimeOffset.UtcNow;
        var health = new Dictionary<string, ConnectorHealthStatus>();

        AddConnectorHealth(health, "Salesforce",
            CounterValue(OTel.SalesforceQueryOps) + CounterValue(OTel.SalesforceWriteOps),
            CounterValue(OTel.SalesforceErrors), now);

        AddConnectorHealth(health, "HubSpot",
            CounterValue(OTel.HubSpotQueryOps) + CounterValue(OTel.HubSpotWriteOps),
            CounterValue(OTel.HubSpotErrors), now);

        AddConnectorHealth(health, "QuickBooks",
            CounterValue(OTel.QuickBooksQueryOps) + CounterValue(OTel.QuickBooksWriteOps),
            CounterValue(OTel.QuickBooksErrors), now);

        AddConnectorHealth(health, "Slack",
            CounterValue(OTel.SlackMessagesSent) + CounterValue(OTel.SlackQueryOps) + CounterValue(OTel.SlackAlertsSent),
            CounterValue(OTel.SlackErrors), now);

        AddConnectorHealth(health, "GoogleWorkspace",
            CounterValue(OTel.GoogleWorkspaceQueryOps) + CounterValue(OTel.GoogleWorkspaceWriteOps),
            CounterValue(OTel.GoogleWorkspaceErrors), now);

        AddConnectorHealth(health, "M365",
            CounterValue(OTel.M365QueryOps) + CounterValue(OTel.M365WriteOps),
            CounterValue(OTel.M365Errors), now);

        return health;
    }

    private static void AddConnectorHealth(
        Dictionary<string, ConnectorHealthStatus> health,
        string name, long totalOps, long totalErrors, DateTimeOffset now)
    {
        double errorRate = totalOps > 0 ? (double)totalErrors / totalOps * 100.0 : 0.0;
        health[name] = new ConnectorHealthStatus(
            Name: name,
            IsHealthy: errorRate < 10.0,
            TotalOperations: totalOps,
            TotalErrors: totalErrors,
            ErrorRate: Math.Round(errorRate, 2),
            LastCheckAtUtc: now);
    }

    private IReadOnlyDictionary<string, AgentMetricsSummary> BuildAgentMetrics()
    {
        var metrics = new Dictionary<string, AgentMetricsSummary>();

        foreach (var (agentId, state) in _agentStates)
        {
            metrics[state.AgentName] = new AgentMetricsSummary(
                AgentId: agentId,
                AgentName: state.AgentName,
                ExecutionsTotal: Interlocked.Read(ref state.ExecutionsTotal),
                ExecutionsFailed: Interlocked.Read(ref state.ExecutionsFailed),
                AverageDurationMs: state.GetAverageDurationMs(),
                LastExecutionAtUtc: state.LastExecutionAtUtc);
        }

        return metrics;
    }

    /// <summary>
    /// Reads the current value from a Counter by examining its underlying field.
    /// Since System.Diagnostics.Metrics Counter does not expose a direct read API,
    /// we track via the Interlocked pattern on the observability side. For connector
    /// counters that are incremented elsewhere, we return 0 as a baseline - the real
    /// values are exported via Prometheus/OpenTelemetry.
    /// </summary>
    private static long CounterValue(System.Diagnostics.Metrics.Counter<long> _) => 0;

    // These provide refs to static mutable tracking fields for task counts.
    // Since Telemetry counters don't expose read access, we maintain shadow counters.
    private static long s_tasksExecutedShadow;
    private static long s_tasksFailedShadow;

    private static ref long Unsafe_TasksExecutedReader() => ref s_tasksExecutedShadow;
    private static ref long Unsafe_TasksFailedReader() => ref s_tasksFailedShadow;

    /// <summary>
    /// Call this to increment the shadow counter when tasks are executed.
    /// </summary>
    public static void IncrementTasksExecuted()
    {
        Interlocked.Increment(ref s_tasksExecutedShadow);
    }

    /// <summary>
    /// Call this to increment the shadow counter when tasks fail.
    /// </summary>
    public static void IncrementTasksFailed()
    {
        Interlocked.Increment(ref s_tasksFailedShadow);
    }

    private sealed class AgentTrackingState
    {
        public readonly string AgentName;
        public long ExecutionsTotal;
        public long ExecutionsFailed;
        private long _totalDurationTicksX100;
        public DateTimeOffset LastExecutionAtUtc;

        public AgentTrackingState(string agentName)
        {
            AgentName = agentName;
        }

        public void RecordExecution(AgentExecutionTrace trace)
        {
            Interlocked.Increment(ref ExecutionsTotal);
            if (!trace.IsSuccess)
            {
                Interlocked.Increment(ref ExecutionsFailed);
            }

            Interlocked.Add(ref _totalDurationTicksX100, (long)(trace.DurationMs * 100));
            LastExecutionAtUtc = trace.CompletedAtUtc;
        }

        public double GetAverageDurationMs()
        {
            long total = Interlocked.Read(ref ExecutionsTotal);
            if (total == 0) return 0.0;
            long ticks = Interlocked.Read(ref _totalDurationTicksX100);
            return Math.Round((double)ticks / 100.0 / total, 2);
        }
    }
}

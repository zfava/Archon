using System.Diagnostics;
using System.Text.Json;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Monitoring;
using ArchonAI.Core.Models.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Persistence.Stores;

/// <summary>
/// Postgres-backed monitoring dashboard service. Since monitoring collects live runtime metrics
/// (CPU, memory, GC, thread pool, etc.) that cannot come from a database, this store persists
/// service status counters so they survive restarts, and delegates actual dashboard data
/// collection to the underlying runtime services.
/// </summary>
public sealed class PostgresMonitoringDashboardStore : IMonitoringDashboardService
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly IObservabilityService _observability;
    private readonly ILogger<PostgresMonitoringDashboardStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private string SnapshotsTable => $"{_schema}.monitoring_snapshots";
    private string CountersTable => $"{_schema}.monitoring_counters";

    private long _dashboardsGenerated;
    private long _workflowMetricsQueries;
    private long _agentHealthQueries;
    private long _systemPerformanceQueries;

    public PostgresMonitoringDashboardStore(
        IOptions<PersistenceOptions> options,
        ILogger<PostgresMonitoringDashboardStore> logger,
        IObservabilityService observability)
    {
        _connectionString = options.Value.ConnectionStringHardened
            ?? throw new ArgumentNullException(nameof(options), "ConnectionString is required.");
        _schema = options.Value.Schema;
        _logger = logger;
        _observability = observability;
    }

    // ── Initialization ──────────────────────────────────────────────

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                CREATE SCHEMA IF NOT EXISTS {_schema};

                CREATE TABLE IF NOT EXISTS {SnapshotsTable} (
                    id              uuid PRIMARY KEY,
                    dashboard_type  text NOT NULL,
                    snapshot_data   jsonb NOT NULL,
                    captured_at_utc timestamptz NOT NULL
                );

                CREATE TABLE IF NOT EXISTS {CountersTable} (
                    counter_name    text PRIMARY KEY,
                    counter_value   bigint NOT NULL DEFAULT 0,
                    updated_at_utc  timestamptz NOT NULL
                );
            ";
            await cmd.ExecuteNonQueryAsync(ct);

            // Restore counters from DB
            await RestoreCountersAsync(conn, ct);

            _initialized = true;
            _logger.LogInformation("PostgresMonitoringDashboardStore initialized (schema={Schema}).", _schema);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task RestoreCountersAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            $"SELECT counter_name, counter_value FROM {CountersTable}", conn);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(0);
            var value = reader.GetInt64(1);

            switch (name)
            {
                case "dashboards_generated":
                    Interlocked.Exchange(ref _dashboardsGenerated, value);
                    break;
                case "workflow_metrics_queries":
                    Interlocked.Exchange(ref _workflowMetricsQueries, value);
                    break;
                case "agent_health_queries":
                    Interlocked.Exchange(ref _agentHealthQueries, value);
                    break;
                case "system_performance_queries":
                    Interlocked.Exchange(ref _systemPerformanceQueries, value);
                    break;
            }
        }
    }

    private async Task PersistCounterAsync(string counterName, long value, CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand($@"
                INSERT INTO {CountersTable} (counter_name, counter_value, updated_at_utc)
                VALUES (@name, @value, @now)
                ON CONFLICT (counter_name) DO UPDATE SET
                    counter_value = EXCLUDED.counter_value,
                    updated_at_utc = EXCLUDED.updated_at_utc
            ", conn);

            cmd.Parameters.AddWithValue("name", counterName);
            cmd.Parameters.AddWithValue("value", value);
            cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);

            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist counter {CounterName}.", counterName);
        }
    }

    // ── GetFullDashboardAsync ───────────────────────────────────────

    public async Task<MonitoringDashboard> GetFullDashboardAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var count = Interlocked.Increment(ref _dashboardsGenerated);

        var systemMetrics = await _observability.GetSystemMetricsAsync(ct);
        var connectorHealth = await _observability.GetConnectorHealthAsync(ct);
        var agentMetrics = await _observability.GetAgentMetricsSummariesAsync(ct);

        var systemPerformance = BuildSystemPerformanceDashboard(systemMetrics, connectorHealth);

        var agentHealth = BuildAgentHealthDashboard(agentMetrics);

        var workflowMetrics = new WorkflowMetricsDashboard(
            TotalWorkflowsCreated: 0,
            TotalWorkflowsValidated: 0,
            TotalWorkflowsExecuted: systemMetrics.TotalTasksExecuted,
            TotalWorkflowsFailed: systemMetrics.TotalTasksFailed,
            ExecutionSuccessRate: systemMetrics.TaskSuccessRate,
            AverageExecutionDurationMs: 0.0,
            StatusBreakdown: Array.Empty<WorkflowStatusBreakdown>(),
            RecentExecutions: Array.Empty<RecentWorkflowExecution>(),
            CapturedAtUtc: DateTimeOffset.UtcNow);

        var dashboard = new MonitoringDashboard(
            WorkflowMetrics: workflowMetrics,
            AgentHealth: agentHealth,
            SystemPerformance: systemPerformance,
            GeneratedAtUtc: DateTimeOffset.UtcNow);

        _ = PersistCounterAsync("dashboards_generated", count, ct);

        _logger.LogInformation("Full monitoring dashboard generated.");
        return dashboard;
    }

    // ── GetWorkflowMetricsAsync ─────────────────────────────────────

    public async Task<WorkflowMetricsDashboard> GetWorkflowMetricsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var count = Interlocked.Increment(ref _workflowMetricsQueries);
        _ = PersistCounterAsync("workflow_metrics_queries", count, ct);

        var systemMetrics = await _observability.GetSystemMetricsAsync(ct);

        return new WorkflowMetricsDashboard(
            TotalWorkflowsCreated: 0,
            TotalWorkflowsValidated: 0,
            TotalWorkflowsExecuted: systemMetrics.TotalTasksExecuted,
            TotalWorkflowsFailed: systemMetrics.TotalTasksFailed,
            ExecutionSuccessRate: systemMetrics.TaskSuccessRate,
            AverageExecutionDurationMs: 0.0,
            StatusBreakdown: Array.Empty<WorkflowStatusBreakdown>(),
            RecentExecutions: Array.Empty<RecentWorkflowExecution>(),
            CapturedAtUtc: DateTimeOffset.UtcNow);
    }

    // ── GetAgentHealthAsync ─────────────────────────────────────────

    public async Task<AgentHealthDashboard> GetAgentHealthAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var count = Interlocked.Increment(ref _agentHealthQueries);
        _ = PersistCounterAsync("agent_health_queries", count, ct);

        var agentMetrics = await _observability.GetAgentMetricsSummariesAsync(ct);
        return BuildAgentHealthDashboard(agentMetrics);
    }

    // ── GetSystemPerformanceAsync ───────────────────────────────────

    public async Task<SystemPerformanceDashboard> GetSystemPerformanceAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var count = Interlocked.Increment(ref _systemPerformanceQueries);
        _ = PersistCounterAsync("system_performance_queries", count, ct);

        var systemMetrics = await _observability.GetSystemMetricsAsync(ct);
        var connectorHealth = await _observability.GetConnectorHealthAsync(ct);
        return BuildSystemPerformanceDashboard(systemMetrics, connectorHealth);
    }

    // ── GetStatus ───────────────────────────────────────────────────

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

    // ── Dashboard builders ──────────────────────────────────────────

    private static SystemPerformanceDashboard BuildSystemPerformanceDashboard(
        SystemMetricsSnapshot systemMetrics,
        IReadOnlyDictionary<string, ConnectorHealthStatus> connectorHealth)
    {
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

    private static AgentHealthDashboard BuildAgentHealthDashboard(
        IReadOnlyDictionary<string, AgentMetricsSummary> agentMetrics)
    {
        var agentDetails = new List<AgentHealthDetail>();

        foreach (var (agentName, metrics) in agentMetrics)
        {
            long execTotal = metrics.ExecutionsTotal;
            long execFailed = metrics.ExecutionsFailed;
            double failureRate = execTotal > 0
                ? Math.Round((double)execFailed / execTotal * 100.0, 2)
                : 0.0;

            double avgLatency = metrics.AverageDurationMs;

            var healthStatus = DetermineAgentHealth(failureRate, avgLatency, execTotal);

            agentDetails.Add(new AgentHealthDetail(
                AgentId: Guid.Empty,
                AgentName: agentName,
                Version: "unknown",
                IsEnabled: true,
                HealthStatus: healthStatus,
                ExecutionsTotal: execTotal,
                ExecutionsFailed: execFailed,
                FailureRate: failureRate,
                AverageLatencyMs: Math.Round(avgLatency, 2),
                Capabilities: Array.Empty<string>(),
                LastActivityAtUtc: metrics.LastExecutionAtUtc));
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
        if (executionCount == 0) return AgentHealthStatus.Unknown;
        if (failureRate > 25.0 || avgLatencyMs > 30_000) return AgentHealthStatus.Unhealthy;
        if (failureRate > 10.0 || avgLatencyMs > 10_000) return AgentHealthStatus.Degraded;
        return AgentHealthStatus.Healthy;
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

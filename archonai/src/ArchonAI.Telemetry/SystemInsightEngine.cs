using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Telemetry;

namespace ArchonAI.Telemetry;

public sealed class SystemInsightEngine : ISystemInsightEngine
{
    private readonly IModelPerformanceTracker _modelTracker;
    private readonly ConcurrentDictionary<Guid, AgentLoadState> _agentLoad = new();
    private readonly ConcurrentDictionary<string, ModelLatencyState> _modelLatency = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<DetectedAnomaly> _anomalies = new();
    private readonly TelemetryOptions _options;

    private const int MaxAnomalyHistory = 500;
    private const int MaxLatencySamples = 1000;
    private const double HighLoadThreshold = 0.8;
    private const double CriticalLoadThreshold = 0.95;
    private const double AnomalyDeviationThreshold = 2.5;

    public SystemInsightEngine(
        IModelPerformanceTracker modelTracker,
        Microsoft.Extensions.Options.IOptions<TelemetryOptions> options)
    {
        _modelTracker = modelTracker;
        _options = options.Value;
    }

    public void RecordAgentLoad(Guid agentId, string agentName, int activeTasks, int queuedTasks,
        double executionTimeMs, double cpuPercent, double memoryPercent)
    {
        _agentLoad.AddOrUpdate(agentId,
            _ => new AgentLoadState(agentName, activeTasks, queuedTasks, executionTimeMs, cpuPercent, memoryPercent),
            (_, existing) =>
            {
                existing.Update(activeTasks, queuedTasks, executionTimeMs, cpuPercent, memoryPercent);
                return existing;
            });
    }

    public void RecordModelLatency(string provider, string model, double latencyMs, bool success)
    {
        string key = $"{provider}::{model}";
        _modelLatency.AddOrUpdate(key,
            _ => new ModelLatencyState(provider, model, latencyMs, success),
            (_, existing) =>
            {
                existing.Record(latencyMs, success);
                return existing;
            });
    }

    public async global::System.Threading.Tasks.Task<SystemInsightDashboard> GetDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var bottlenecks = await DetectBottlenecksAsync(cancellationToken);
        var agentLoad = await GetAgentLoadAsync(cancellationToken);
        var modelLatency = await GetModelLatencyAsync(cancellationToken);
        var anomalies = await DetectAnomaliesAsync(cancellationToken);
        var health = await GetHealthSummaryAsync(cancellationToken);

        return new SystemInsightDashboard(
            Bottlenecks: bottlenecks,
            AgentLoad: agentLoad,
            ModelLatency: modelLatency,
            Anomalies: anomalies,
            HealthSummary: health,
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<BottleneckReport>> DetectBottlenecksAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var bottlenecks = new List<BottleneckReport>();
        var now = DateTimeOffset.UtcNow;

        // Agent bottlenecks
        foreach (var (agentId, state) in _agentLoad)
        {
            double loadRatio = state.CpuPercent / 100.0;
            if (loadRatio >= HighLoadThreshold)
            {
                bottlenecks.Add(new BottleneckReport(
                    Component: $"agent:{state.AgentName}",
                    Category: "agent-overload",
                    Description: $"Agent '{state.AgentName}' CPU at {state.CpuPercent:F1}% with {state.ActiveTasks} active tasks.",
                    SeverityScore: Math.Min(1.0, loadRatio),
                    Metrics: new Dictionary<string, string>
                    {
                        ["agentId"] = agentId.ToString(),
                        ["cpuPercent"] = state.CpuPercent.ToString("F1"),
                        ["memoryPercent"] = state.MemoryPercent.ToString("F1"),
                        ["activeTasks"] = state.ActiveTasks.ToString(),
                        ["queuedTasks"] = state.QueuedTasks.ToString()
                    },
                    DetectedAtUtc: now));
            }

            if (state.QueuedTasks > state.ActiveTasks * 2 && state.QueuedTasks > 5)
            {
                bottlenecks.Add(new BottleneckReport(
                    Component: $"agent:{state.AgentName}",
                    Category: "queue-backlog",
                    Description: $"Agent '{state.AgentName}' has {state.QueuedTasks} queued tasks vs {state.ActiveTasks} active.",
                    SeverityScore: Math.Min(1.0, (double)state.QueuedTasks / Math.Max(1, state.ActiveTasks * 5)),
                    Metrics: new Dictionary<string, string>
                    {
                        ["agentId"] = agentId.ToString(),
                        ["queuedTasks"] = state.QueuedTasks.ToString(),
                        ["activeTasks"] = state.ActiveTasks.ToString()
                    },
                    DetectedAtUtc: now));
            }
        }

        // Model latency bottlenecks
        foreach (var (_, state) in _modelLatency)
        {
            double p95 = state.GetP95();
            if (p95 > 5000) // > 5s P95
            {
                bottlenecks.Add(new BottleneckReport(
                    Component: $"model:{state.Provider}/{state.Model}",
                    Category: "model-latency",
                    Description: $"Model '{state.Provider}/{state.Model}' P95 latency at {p95:F0}ms.",
                    SeverityScore: Math.Min(1.0, p95 / 10000.0),
                    Metrics: new Dictionary<string, string>
                    {
                        ["provider"] = state.Provider,
                        ["model"] = state.Model,
                        ["p95Ms"] = p95.ToString("F0"),
                        ["avgMs"] = state.GetAverage().ToString("F0"),
                        ["errorRate"] = state.GetErrorRate().ToString("F3")
                    },
                    DetectedAtUtc: now));
            }

            double errorRate = state.GetErrorRate();
            if (errorRate > 0.1 && state.TotalRequests > 10) // > 10% errors
            {
                bottlenecks.Add(new BottleneckReport(
                    Component: $"model:{state.Provider}/{state.Model}",
                    Category: "model-errors",
                    Description: $"Model '{state.Provider}/{state.Model}' error rate at {errorRate:P1}.",
                    SeverityScore: Math.Min(1.0, errorRate * 2),
                    Metrics: new Dictionary<string, string>
                    {
                        ["provider"] = state.Provider,
                        ["model"] = state.Model,
                        ["errorRate"] = errorRate.ToString("F3"),
                        ["totalRequests"] = state.TotalRequests.ToString(),
                        ["failedRequests"] = state.FailedRequests.ToString()
                    },
                    DetectedAtUtc: now));
            }
        }

        IReadOnlyList<BottleneckReport> result = bottlenecks
            .OrderByDescending(b => b.SeverityScore)
            .ToList();

        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<DetectedAnomaly>> DetectAnomaliesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var newAnomalies = new List<DetectedAnomaly>();
        var now = DateTimeOffset.UtcNow;

        // Detect agent load anomalies
        var agentStates = _agentLoad.Values.ToList();
        if (agentStates.Count >= 3)
        {
            double meanCpu = agentStates.Average(a => a.CpuPercent);
            double stdDevCpu = Math.Sqrt(agentStates.Average(a => Math.Pow(a.CpuPercent - meanCpu, 2)));

            foreach (var (agentId, state) in _agentLoad)
            {
                if (stdDevCpu > 0 && Math.Abs(state.CpuPercent - meanCpu) > stdDevCpu * AnomalyDeviationThreshold)
                {
                    var anomaly = new DetectedAnomaly(
                        Id: Guid.NewGuid(),
                        Category: "agent-load",
                        Component: $"agent:{state.AgentName}",
                        Description: $"Agent '{state.AgentName}' CPU usage ({state.CpuPercent:F1}%) deviates significantly from mean ({meanCpu:F1}%).",
                        AnomalyScore: Math.Abs(state.CpuPercent - meanCpu) / Math.Max(1, stdDevCpu),
                        ExpectedValue: meanCpu,
                        ActualValue: state.CpuPercent,
                        Severity: state.CpuPercent > meanCpu ? "high" : "medium",
                        IsResolved: false,
                        DetectedAtUtc: now,
                        ResolvedAtUtc: null);

                    newAnomalies.Add(anomaly);
                    EnqueueAnomaly(anomaly);
                }
            }
        }

        // Detect model latency anomalies
        var modelStates = _modelLatency.Values.Where(m => m.TotalRequests >= 10).ToList();
        if (modelStates.Count >= 2)
        {
            double meanLatency = modelStates.Average(m => m.GetAverage());
            double stdDevLatency = Math.Sqrt(modelStates.Average(m => Math.Pow(m.GetAverage() - meanLatency, 2)));

            foreach (var state in modelStates)
            {
                double avg = state.GetAverage();
                if (stdDevLatency > 0 && Math.Abs(avg - meanLatency) > stdDevLatency * AnomalyDeviationThreshold)
                {
                    var anomaly = new DetectedAnomaly(
                        Id: Guid.NewGuid(),
                        Category: "model-latency",
                        Component: $"model:{state.Provider}/{state.Model}",
                        Description: $"Model '{state.Provider}/{state.Model}' average latency ({avg:F0}ms) deviates from mean ({meanLatency:F0}ms).",
                        AnomalyScore: Math.Abs(avg - meanLatency) / Math.Max(1, stdDevLatency),
                        ExpectedValue: meanLatency,
                        ActualValue: avg,
                        Severity: avg > meanLatency ? "high" : "low",
                        IsResolved: false,
                        DetectedAtUtc: now,
                        ResolvedAtUtc: null);

                    newAnomalies.Add(anomaly);
                    EnqueueAnomaly(anomaly);
                }
            }
        }

        // Detect model error rate anomalies
        foreach (var state in modelStates)
        {
            double errorRate = state.GetErrorRate();
            if (errorRate > 0.2 && state.TotalRequests >= 20)
            {
                var anomaly = new DetectedAnomaly(
                    Id: Guid.NewGuid(),
                    Category: "model-errors",
                    Component: $"model:{state.Provider}/{state.Model}",
                    Description: $"Model '{state.Provider}/{state.Model}' error rate ({errorRate:P1}) exceeds threshold.",
                    AnomalyScore: errorRate * 5,
                    ExpectedValue: 0.05,
                    ActualValue: errorRate,
                    Severity: errorRate > 0.5 ? "critical" : "high",
                    IsResolved: false,
                    DetectedAtUtc: now,
                    ResolvedAtUtc: null);

                newAnomalies.Add(anomaly);
                EnqueueAnomaly(anomaly);
            }
        }

        IReadOnlyList<DetectedAnomaly> result = newAnomalies
            .OrderByDescending(a => a.AnomalyScore)
            .ToList();

        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<AgentLoadSnapshot>> GetAgentLoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<AgentLoadSnapshot> snapshots = _agentLoad
            .Select(kv => new AgentLoadSnapshot(
                AgentId: kv.Key,
                AgentName: kv.Value.AgentName,
                ActiveTasks: kv.Value.ActiveTasks,
                QueuedTasks: kv.Value.QueuedTasks,
                AverageExecutionTimeMs: kv.Value.GetAverageExecutionTime(),
                CpuUtilizationPercent: kv.Value.CpuPercent,
                MemoryUtilizationPercent: kv.Value.MemoryPercent,
                LoadLevel: ClassifyLoadLevel(kv.Value.CpuPercent / 100.0),
                SnapshotAtUtc: now))
            .OrderByDescending(s => s.CpuUtilizationPercent)
            .ToList();

        return global::System.Threading.Tasks.Task.FromResult(snapshots);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<ModelLatencySnapshot>> GetModelLatencyAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow;

        // Merge with IModelPerformanceTracker data
        var trackerScores = _modelTracker.GetAllScores()
            .ToDictionary(s => $"{s.Provider}::{s.Model}", StringComparer.OrdinalIgnoreCase);

        var snapshots = new List<ModelLatencySnapshot>();

        foreach (var (key, state) in _modelLatency)
        {
            double avg = state.GetAverage();
            double p50 = state.GetPercentile(50);
            double p95 = state.GetP95();
            double p99 = state.GetPercentile(99);
            double errorRate = state.GetErrorRate();

            snapshots.Add(new ModelLatencySnapshot(
                Provider: state.Provider,
                Model: state.Model,
                AverageLatencyMs: avg,
                P50LatencyMs: p50,
                P95LatencyMs: p95,
                P99LatencyMs: p99,
                RequestCount: state.TotalRequests,
                ErrorRate: errorRate,
                HealthStatus: ClassifyModelHealth(avg, errorRate),
                SnapshotAtUtc: now));
        }

        // Add models from tracker that we haven't seen directly
        foreach (var score in trackerScores)
        {
            if (!_modelLatency.ContainsKey(score.Key))
            {
                snapshots.Add(new ModelLatencySnapshot(
                    Provider: score.Value.Provider,
                    Model: score.Value.Model,
                    AverageLatencyMs: score.Value.AverageLatencyMs,
                    P50LatencyMs: score.Value.AverageLatencyMs,
                    P95LatencyMs: score.Value.AverageLatencyMs * 2,
                    P99LatencyMs: score.Value.AverageLatencyMs * 3,
                    RequestCount: score.Value.SampleCount,
                    ErrorRate: 1.0 - score.Value.SuccessRate,
                    HealthStatus: ClassifyModelHealth(score.Value.AverageLatencyMs, 1.0 - score.Value.SuccessRate),
                    SnapshotAtUtc: now));
            }
        }

        IReadOnlyList<ModelLatencySnapshot> result = snapshots
            .OrderByDescending(s => s.AverageLatencyMs)
            .ToList();

        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public async global::System.Threading.Tasks.Task<SystemHealthSummary> GetHealthSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var agentSnapshots = await GetAgentLoadAsync(cancellationToken);
        var modelSnapshots = await GetModelLatencyAsync(cancellationToken);
        var bottlenecks = await DetectBottlenecksAsync(cancellationToken);

        int unresolvedAnomalies = _anomalies.Count(a => !a.IsResolved);
        double avgAgentLoad = agentSnapshots.Count > 0
            ? agentSnapshots.Average(a => a.CpuUtilizationPercent)
            : 0;
        double avgModelLatency = modelSnapshots.Count > 0
            ? modelSnapshots.Average(m => m.AverageLatencyMs)
            : 0;

        // Health score: 40% agent load inverse + 30% model health + 30% anomaly/bottleneck penalty
        double agentHealthComponent = agentSnapshots.Count > 0
            ? Math.Max(0, 1.0 - avgAgentLoad / 100.0)
            : 1.0;
        double modelHealthComponent = modelSnapshots.Count > 0
            ? modelSnapshots.Average(m => m.ErrorRate < 0.05 ? 1.0 : m.ErrorRate < 0.1 ? 0.7 : 0.3)
            : 1.0;
        double issuesPenalty = Math.Max(0, 1.0 - (bottlenecks.Count * 0.1 + unresolvedAnomalies * 0.05));

        double overallHealth = agentHealthComponent * 0.4 + modelHealthComponent * 0.3 + issuesPenalty * 0.3;

        return new SystemHealthSummary(
            OverallHealthScore: Math.Round(overallHealth, 3),
            TotalActiveAgents: agentSnapshots.Count,
            TotalActiveModels: modelSnapshots.Count,
            OpenBottlenecks: bottlenecks.Count,
            UnresolvedAnomalies: unresolvedAnomalies,
            AverageAgentLoad: Math.Round(avgAgentLoad, 1),
            AverageModelLatencyMs: Math.Round(avgModelLatency, 1),
            HealthStatus: overallHealth >= 0.8 ? "healthy" : overallHealth >= 0.5 ? "degraded" : "critical",
            EvaluatedAtUtc: DateTimeOffset.UtcNow);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<InsightTrend>> GetTrendsAsync(
        string? component = null, int dataPointLimit = 60,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var trends = new List<InsightTrend>();
        var now = DateTimeOffset.UtcNow;

        // Agent CPU trends
        foreach (var (agentId, state) in _agentLoad)
        {
            if (component is not null && !$"agent:{state.AgentName}".Contains(component, StringComparison.OrdinalIgnoreCase))
                continue;

            var dataPoints = state.GetCpuHistory(dataPointLimit);
            if (dataPoints.Count >= 2)
            {
                double trendDirection = dataPoints[^1].Value - dataPoints[0].Value;
                trends.Add(new InsightTrend(
                    MetricName: "cpu-utilization",
                    Component: $"agent:{state.AgentName}",
                    DataPoints: dataPoints,
                    TrendDirection: trendDirection,
                    TrendLabel: trendDirection > 5 ? "increasing" : trendDirection < -5 ? "decreasing" : "stable",
                    FromUtc: dataPoints[0].TimestampUtc,
                    ToUtc: dataPoints[^1].TimestampUtc));
            }
        }

        // Model latency trends
        foreach (var (_, state) in _modelLatency)
        {
            if (component is not null && !$"model:{state.Provider}/{state.Model}".Contains(component, StringComparison.OrdinalIgnoreCase))
                continue;

            var dataPoints = state.GetLatencyHistory(dataPointLimit);
            if (dataPoints.Count >= 2)
            {
                double trendDirection = dataPoints[^1].Value - dataPoints[0].Value;
                trends.Add(new InsightTrend(
                    MetricName: "latency-ms",
                    Component: $"model:{state.Provider}/{state.Model}",
                    DataPoints: dataPoints,
                    TrendDirection: trendDirection,
                    TrendLabel: trendDirection > 100 ? "increasing" : trendDirection < -100 ? "decreasing" : "stable",
                    FromUtc: dataPoints[0].TimestampUtc,
                    ToUtc: dataPoints[^1].TimestampUtc));
            }
        }

        IReadOnlyList<InsightTrend> result = trends;
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    private void EnqueueAnomaly(DetectedAnomaly anomaly)
    {
        _anomalies.Enqueue(anomaly);
        while (_anomalies.Count > MaxAnomalyHistory)
        {
            _anomalies.TryDequeue(out _);
        }
    }

    private static string ClassifyLoadLevel(double loadRatio) => loadRatio switch
    {
        >= CriticalLoadThreshold => "critical",
        >= HighLoadThreshold => "high",
        >= 0.5 => "moderate",
        _ => "low"
    };

    private static string ClassifyModelHealth(double avgLatencyMs, double errorRate)
    {
        if (errorRate > 0.2) return "critical";
        if (errorRate > 0.1 || avgLatencyMs > 10000) return "degraded";
        if (avgLatencyMs > 5000) return "slow";
        return "healthy";
    }

    private sealed class AgentLoadState
    {
        public string AgentName { get; }
        public int ActiveTasks { get; private set; }
        public int QueuedTasks { get; private set; }
        public double CpuPercent { get; private set; }
        public double MemoryPercent { get; private set; }

        private readonly ConcurrentQueue<(double executionTimeMs, DateTimeOffset timestamp)> _executionTimes = new();
        private readonly ConcurrentQueue<InsightDataPoint> _cpuHistory = new();
        private const int MaxSamples = 1000;

        public AgentLoadState(string agentName, int activeTasks, int queuedTasks,
            double executionTimeMs, double cpuPercent, double memoryPercent)
        {
            AgentName = agentName;
            ActiveTasks = activeTasks;
            QueuedTasks = queuedTasks;
            CpuPercent = cpuPercent;
            MemoryPercent = memoryPercent;
            _executionTimes.Enqueue((executionTimeMs, DateTimeOffset.UtcNow));
            _cpuHistory.Enqueue(new InsightDataPoint(cpuPercent, DateTimeOffset.UtcNow));
        }

        public void Update(int activeTasks, int queuedTasks, double executionTimeMs,
            double cpuPercent, double memoryPercent)
        {
            ActiveTasks = activeTasks;
            QueuedTasks = queuedTasks;
            CpuPercent = cpuPercent;
            MemoryPercent = memoryPercent;
            _executionTimes.Enqueue((executionTimeMs, DateTimeOffset.UtcNow));
            _cpuHistory.Enqueue(new InsightDataPoint(cpuPercent, DateTimeOffset.UtcNow));

            while (_executionTimes.Count > MaxSamples) _executionTimes.TryDequeue(out _);
            while (_cpuHistory.Count > MaxSamples) _cpuHistory.TryDequeue(out _);
        }

        public double GetAverageExecutionTime()
        {
            var samples = _executionTimes.ToArray();
            return samples.Length == 0 ? 0 : samples.Average(s => s.executionTimeMs);
        }

        public IReadOnlyList<InsightDataPoint> GetCpuHistory(int limit)
        {
            return _cpuHistory.ToArray().TakeLast(limit).ToList();
        }
    }

    private sealed class ModelLatencyState
    {
        public string Provider { get; }
        public string Model { get; }
        public int TotalRequests => (int)Interlocked.Read(ref _totalRequests);
        public int FailedRequests => (int)Interlocked.Read(ref _failedRequests);

        private long _totalRequests;
        private long _failedRequests;
        private readonly ConcurrentQueue<double> _latencies = new();
        private readonly ConcurrentQueue<InsightDataPoint> _latencyHistory = new();

        public ModelLatencyState(string provider, string model, double latencyMs, bool success)
        {
            Provider = provider;
            Model = model;
            _totalRequests = 1;
            _failedRequests = success ? 0 : 1;
            _latencies.Enqueue(latencyMs);
            _latencyHistory.Enqueue(new InsightDataPoint(latencyMs, DateTimeOffset.UtcNow));
        }

        public void Record(double latencyMs, bool success)
        {
            Interlocked.Increment(ref _totalRequests);
            if (!success) Interlocked.Increment(ref _failedRequests);
            _latencies.Enqueue(latencyMs);
            _latencyHistory.Enqueue(new InsightDataPoint(latencyMs, DateTimeOffset.UtcNow));

            while (_latencies.Count > MaxLatencySamples) _latencies.TryDequeue(out _);
            while (_latencyHistory.Count > MaxLatencySamples) _latencyHistory.TryDequeue(out _);
        }

        public double GetAverage()
        {
            var samples = _latencies.ToArray();
            return samples.Length == 0 ? 0 : samples.Average();
        }

        public double GetP95() => GetPercentile(95);

        public double GetPercentile(int percentile)
        {
            var sorted = _latencies.ToArray().OrderBy(l => l).ToArray();
            if (sorted.Length == 0) return 0;
            int index = Math.Min((int)Math.Ceiling(sorted.Length * percentile / 100.0) - 1, sorted.Length - 1);
            return sorted[Math.Max(0, index)];
        }

        public double GetErrorRate()
        {
            long total = Interlocked.Read(ref _totalRequests);
            return total == 0 ? 0 : (double)Interlocked.Read(ref _failedRequests) / total;
        }

        public IReadOnlyList<InsightDataPoint> GetLatencyHistory(int limit)
        {
            return _latencyHistory.ToArray().TakeLast(limit).ToList();
        }
    }
}

using ArchonAI.Core.Models.Telemetry;

namespace ArchonAI.Core.Interfaces;

public interface ISystemInsightEngine
{
    void RecordAgentLoad(Guid agentId, string agentName, int activeTasks, int queuedTasks,
        double executionTimeMs, double cpuPercent, double memoryPercent);

    void RecordModelLatency(string provider, string model, double latencyMs, bool success);

    global::System.Threading.Tasks.Task<SystemInsightDashboard> GetDashboardAsync(
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<BottleneckReport>> DetectBottlenecksAsync(
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<DetectedAnomaly>> DetectAnomaliesAsync(
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<AgentLoadSnapshot>> GetAgentLoadAsync(
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<ModelLatencySnapshot>> GetModelLatencyAsync(
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<SystemHealthSummary> GetHealthSummaryAsync(
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<InsightTrend>> GetTrendsAsync(
        string? component = null, int dataPointLimit = 60,
        CancellationToken cancellationToken = default);
}

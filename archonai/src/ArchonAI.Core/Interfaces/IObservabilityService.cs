using ArchonAI.Core.Models.Observability;

namespace ArchonAI.Core.Interfaces;

public interface IObservabilityService
{
    global::System.Threading.Tasks.Task RecordAgentExecutionAsync(AgentExecutionTrace trace, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<IReadOnlyList<AgentExecutionTrace>> GetAgentTracesAsync(Guid? agentId = null, int limit = 100, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<WorkflowPerformanceSnapshot?> GetWorkflowPerformanceAsync(Guid workflowId, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<SystemMetricsSnapshot> GetSystemMetricsAsync(CancellationToken ct = default);
    global::System.Threading.Tasks.Task<IReadOnlyDictionary<string, ConnectorHealthStatus>> GetConnectorHealthAsync(CancellationToken ct = default);
    global::System.Threading.Tasks.Task<IReadOnlyDictionary<string, AgentMetricsSummary>> GetAgentMetricsSummariesAsync(CancellationToken ct = default);
    ObservabilityServiceStatus GetStatus();
}

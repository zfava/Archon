namespace ArchonAI.Core.Models.Admin;

public sealed record AgentInfo(
    Guid Id,
    string Name,
    string Version,
    IReadOnlyList<string> Capabilities,
    bool IsEnabled,
    long ExecutionCount,
    long FailureCount,
    DateTimeOffset RegisteredAtUtc);

public sealed record WorkflowInfo(
    Guid Id,
    string Name,
    string Status,
    string Template,
    int TotalSteps,
    int CompletedSteps,
    int FailedSteps,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record PolicyConfiguration(
    IReadOnlyList<string> ForbiddenCapabilities,
    IReadOnlyList<string> HighRiskCapabilities,
    IReadOnlyList<string> ApprovalCheckpointCapabilities,
    double MinConfidenceThreshold,
    int AutoBlockRiskThreshold,
    int ApprovalRiskThreshold,
    bool RequireApprovalForHighRisk);

public sealed record SystemMonitoringSnapshot(
    int ActiveAgents,
    int RunningWorkflows,
    long TotalTasksExecuted,
    long TotalTasksFailed,
    double SystemCpuPercent,
    long SystemMemoryMb,
    IReadOnlyDictionary<string, string> ConnectorStatuses,
    IReadOnlyDictionary<string, string> AdditionalMetrics,
    DateTimeOffset CapturedAtUtc);

public sealed record AdminServiceStatus(
    bool IsActive,
    long AgentQueries,
    long WorkflowQueries,
    long PolicyUpdates,
    long MonitoringSnapshots,
    DateTimeOffset StatusAsOfUtc);

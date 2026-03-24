using ArchonAI.Core.Models.ControlPlane;

namespace ArchonAI.Core.Interfaces;

public interface IControlPlaneObservability
{
    // ── Dashboard modules ──────────────────────────────────────

    global::System.Threading.Tasks.Task<AgentActivityDashboard> GetAgentActivityAsync(
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<SystemHealthDashboard> GetSystemHealthAsync(
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<ModelUsageDashboard> GetModelUsageAsync(
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<TaskPerformanceDashboard> GetTaskPerformanceAsync(
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<UnifiedControlPlaneDashboard> GetUnifiedDashboardAsync(
        CancellationToken ct = default);

    // ── System control ─────────────────────────────────────────

    global::System.Threading.Tasks.Task PauseSystemAsync(string reason,
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task ResumeSystemAsync(
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task<bool> IsSystemPausedAsync(
        CancellationToken ct = default);

    // ── Alert management ───────────────────────────────────────

    global::System.Threading.Tasks.Task<IReadOnlyList<SystemAlert>> GetActiveAlertsAsync(
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task AcknowledgeAlertAsync(Guid alertId,
        CancellationToken ct = default);

    void RaiseAlert(string severity, string component, string message);
}

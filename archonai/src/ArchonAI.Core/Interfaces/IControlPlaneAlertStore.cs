using ArchonAI.Core.Models.ControlPlane;

namespace ArchonAI.Core.Interfaces;

/// <summary>
/// Durable store for control plane operational state that must be
/// consistent across multiple application instances: system pause state,
/// active alerts, and recent agent activity events.
/// </summary>
public interface IControlPlaneAlertStore
{
    // ── System pause state ──────────────────────────────────────

    global::System.Threading.Tasks.Task SetPauseStateAsync(
        bool isPaused, string? reason, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<(bool IsPaused, string? Reason)> GetPauseStateAsync(
        CancellationToken ct = default);

    // ── Alert management ────────────────────────────────────────

    global::System.Threading.Tasks.Task UpsertAlertAsync(
        SystemAlert alert, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<SystemAlert>> GetActiveAlertsAsync(
        CancellationToken ct = default);

    global::System.Threading.Tasks.Task AcknowledgeAlertAsync(
        Guid alertId, CancellationToken ct = default);

    global::System.Threading.Tasks.Task EvictStaleAlertsAsync(
        int maxAlerts = 500, CancellationToken ct = default);

    // ── Recent agent activity events ────────────────────────────

    global::System.Threading.Tasks.Task AddAgentEventAsync(
        AgentActivityEvent evt, CancellationToken ct = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<AgentActivityEvent>> GetRecentEventsAsync(
        int limit = 50, CancellationToken ct = default);
}

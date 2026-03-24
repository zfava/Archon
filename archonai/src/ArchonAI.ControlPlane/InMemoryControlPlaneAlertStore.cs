using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.ControlPlane;

namespace ArchonAI.ControlPlane;

/// <summary>
/// In-memory fallback implementation of <see cref="IControlPlaneAlertStore"/>.
/// Suitable for single-instance development only — state is not shared across
/// instances and is lost on restart. Use the PostgreSQL-backed implementation
/// for production multi-instance deployments.
/// </summary>
public sealed class InMemoryControlPlaneAlertStore : IControlPlaneAlertStore
{
    private readonly ConcurrentQueue<AgentActivityEvent> _recentEvents = new();
    private readonly ConcurrentDictionary<Guid, SystemAlert> _activeAlerts = new();
    private volatile bool _systemPaused;
    private string? _pauseReason;

    private const int MaxRecentEvents = 200;

    public Task SetPauseStateAsync(bool isPaused, string? reason, CancellationToken ct = default)
    {
        _systemPaused = isPaused;
        _pauseReason = reason;
        return Task.CompletedTask;
    }

    public Task<(bool IsPaused, string? Reason)> GetPauseStateAsync(CancellationToken ct = default)
    {
        return Task.FromResult((_systemPaused, _pauseReason));
    }

    public Task UpsertAlertAsync(SystemAlert alert, CancellationToken ct = default)
    {
        _activeAlerts[alert.AlertId] = alert;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SystemAlert>> GetActiveAlertsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<SystemAlert> alerts = _activeAlerts.Values
            .Where(a => !a.IsAcknowledged)
            .OrderByDescending(a => a.RaisedAtUtc)
            .ToList();
        return Task.FromResult(alerts);
    }

    public Task AcknowledgeAlertAsync(Guid alertId, CancellationToken ct = default)
    {
        if (_activeAlerts.TryGetValue(alertId, out var alert))
            _activeAlerts[alertId] = alert with { IsAcknowledged = true };
        return Task.CompletedTask;
    }

    public Task EvictStaleAlertsAsync(int maxAlerts = 500, CancellationToken ct = default)
    {
        if (_activeAlerts.Count > maxAlerts * 2)
        {
            var stale = _activeAlerts.Values
                .Where(a => a.IsAcknowledged)
                .OrderBy(a => a.RaisedAtUtc)
                .Take(_activeAlerts.Count - maxAlerts)
                .ToList();

            foreach (var s in stale)
                _activeAlerts.TryRemove(s.AlertId, out _);
        }
        return Task.CompletedTask;
    }

    public Task AddAgentEventAsync(AgentActivityEvent evt, CancellationToken ct = default)
    {
        _recentEvents.Enqueue(evt);
        while (_recentEvents.Count > MaxRecentEvents)
            _recentEvents.TryDequeue(out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AgentActivityEvent>> GetRecentEventsAsync(
        int limit = 50, CancellationToken ct = default)
    {
        IReadOnlyList<AgentActivityEvent> events = _recentEvents.ToArray()
            .TakeLast(limit)
            .Reverse()
            .ToList();
        return Task.FromResult(events);
    }
}

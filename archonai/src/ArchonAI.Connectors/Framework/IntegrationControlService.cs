using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Connector;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Connectors.Framework;

/// <summary>
/// Integration control center. Aggregates health status, sync history,
/// webhook events, and credential warnings across all registered connectors.
/// </summary>
public sealed class IntegrationControlService : IIntegrationControlService
{
    private readonly IEnumerable<IConnector> _connectors;
    private readonly ILogger<IntegrationControlService> _logger;

    private readonly ConcurrentQueue<SyncEvent> _syncEvents = new();
    private readonly ConcurrentQueue<WebhookEvent> _webhookEvents = new();
    private const int MaxEventHistory = 1000;

    public IntegrationControlService(
        IEnumerable<IConnector> connectors,
        ILogger<IntegrationControlService> logger)
    {
        _connectors = connectors;
        _logger = logger;
    }

    public global::System.Threading.Tasks.Task<IntegrationDashboard> GetDashboardAsync(CancellationToken ct = default)
    {
        var healthReports = _connectors
            .Select(ConnectorHealthAdapter.GetHealthReport)
            .ToList();

        var recentSyncs = _syncEvents
            .OrderByDescending(e => e.StartedAtUtc)
            .Take(20)
            .ToList();

        var failedSyncs = _syncEvents
            .Where(e => e.Status == SyncStatus.Failed)
            .OrderByDescending(e => e.StartedAtUtc)
            .Take(20)
            .ToList();

        var credentialStatuses = healthReports
            .Select(r => new CredentialStatus(
                r.ConnectorName, r.IsAuthenticated || r.TotalRequests > 0,
                r.IsAuthenticated, r.TokenExpiresAtUtc, r.CredentialExpiryWarning,
                r.Status == ConnectorHealthStatus.Unhealthy ? "Authentication failed" : null,
                DateTimeOffset.UtcNow))
            .ToList();

        var dashboard = new IntegrationDashboard(
            ConnectorHealth: healthReports,
            RecentSyncs: recentSyncs,
            FailedSyncs: failedSyncs,
            CredentialStatuses: credentialStatuses,
            TotalConnectors: healthReports.Count,
            HealthyConnectors: healthReports.Count(r => r.Status == ConnectorHealthStatus.Healthy),
            DegradedConnectors: healthReports.Count(r => r.Status == ConnectorHealthStatus.Degraded),
            UnhealthyConnectors: healthReports.Count(r => r.Status == ConnectorHealthStatus.Unhealthy),
            GeneratedAtUtc: DateTimeOffset.UtcNow);

        return global::System.Threading.Tasks.Task.FromResult(dashboard);
    }

    public global::System.Threading.Tasks.Task<ConnectorHealthReport> GetConnectorHealthAsync(
        string connectorName, CancellationToken ct = default)
    {
        var connector = _connectors.FirstOrDefault(c =>
            c.SystemName.Equals(connectorName, StringComparison.OrdinalIgnoreCase));

        if (connector is null)
        {
            return global::System.Threading.Tasks.Task.FromResult(
                new ConnectorHealthReport(connectorName, ConnectorHealthStatus.NotConfigured,
                    false, null, null, false, 0, 0, 0, 0,
                    "Connector not registered", DateTimeOffset.UtcNow));
        }

        return global::System.Threading.Tasks.Task.FromResult(
            ConnectorHealthAdapter.GetHealthReport(connector));
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<SyncEvent>> GetRecentSyncsAsync(
        string? connectorName = null, int limit = 50, CancellationToken ct = default)
    {
        IEnumerable<SyncEvent> query = _syncEvents.OrderByDescending(e => e.StartedAtUtc);
        if (!string.IsNullOrWhiteSpace(connectorName))
            query = query.Where(e => e.ConnectorName.Equals(connectorName, StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<SyncEvent> result = query.Take(limit).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<SyncEvent>> GetFailedSyncsAsync(
        string? connectorName = null, int limit = 50, CancellationToken ct = default)
    {
        IEnumerable<SyncEvent> query = _syncEvents
            .Where(e => e.Status == SyncStatus.Failed)
            .OrderByDescending(e => e.StartedAtUtc);

        if (!string.IsNullOrWhiteSpace(connectorName))
            query = query.Where(e => e.ConnectorName.Equals(connectorName, StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<SyncEvent> result = query.Take(limit).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<WebhookEvent>> GetWebhookEventsAsync(
        string? connectorName = null, int limit = 50, CancellationToken ct = default)
    {
        IEnumerable<WebhookEvent> query = _webhookEvents.OrderByDescending(e => e.ReceivedAtUtc);
        if (!string.IsNullOrWhiteSpace(connectorName))
            query = query.Where(e => e.ConnectorName.Equals(connectorName, StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<WebhookEvent> result = query.Take(limit).ToList();
        return global::System.Threading.Tasks.Task.FromResult(result);
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<CredentialStatus>> GetCredentialStatusesAsync(
        CancellationToken ct = default)
    {
        IReadOnlyList<CredentialStatus> statuses = _connectors
            .Select(c =>
            {
                var report = ConnectorHealthAdapter.GetHealthReport(c);
                return new CredentialStatus(
                    report.ConnectorName,
                    report.IsAuthenticated || report.TotalRequests > 0,
                    report.IsAuthenticated,
                    report.TokenExpiresAtUtc,
                    report.CredentialExpiryWarning,
                    report.Status == ConnectorHealthStatus.Unhealthy ? "Authentication failed" : null,
                    DateTimeOffset.UtcNow);
            })
            .ToList();

        return global::System.Threading.Tasks.Task.FromResult(statuses);
    }

    public void RecordSyncEvent(SyncEvent syncEvent)
    {
        _syncEvents.Enqueue(syncEvent);
        while (_syncEvents.Count > MaxEventHistory && _syncEvents.TryDequeue(out _)) { }

        if (syncEvent.Status == SyncStatus.Failed)
        {
            _logger.LogWarning(
                "Sync failed for {Connector}: {Operation} - {Error} ({Category})",
                syncEvent.ConnectorName, syncEvent.OperationType,
                syncEvent.ErrorMessage, syncEvent.ErrorCategory);
        }
    }

    public void RecordWebhookEvent(WebhookEvent webhookEvent)
    {
        _webhookEvents.Enqueue(webhookEvent);
        while (_webhookEvents.Count > MaxEventHistory && _webhookEvents.TryDequeue(out _)) { }

        if (!webhookEvent.SignatureValid)
        {
            _logger.LogWarning(
                "Invalid webhook signature for {Connector}: {EventType}",
                webhookEvent.ConnectorName, webhookEvent.EventType);
        }
    }
}

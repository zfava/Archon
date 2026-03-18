using ArchonAI.Core.Models.Connector;

namespace ArchonAI.Core.Interfaces;

/// <summary>
/// Standardized health reporting contract for all enterprise connectors.
/// Implemented by connectors that support health checks, credential validation,
/// and operational status reporting.
/// </summary>
public interface IConnectorHealth
{
    /// <summary>Returns a standardized health report for this connector.</summary>
    ConnectorHealthReport GetHealthReport();

    /// <summary>Validates that credentials are configured and can authenticate.</summary>
    global::System.Threading.Tasks.Task<CredentialStatus> ValidateCredentialsAsync(CancellationToken ct = default);

    /// <summary>Classifies an HTTP status code into a connector error category.</summary>
    ConnectorErrorCategory ClassifyError(int httpStatusCode, string? errorBody = null);
}

/// <summary>
/// Integration control center service. Provides operational visibility
/// across all registered connectors.
/// </summary>
public interface IIntegrationControlService
{
    global::System.Threading.Tasks.Task<IntegrationDashboard> GetDashboardAsync(CancellationToken ct = default);
    global::System.Threading.Tasks.Task<ConnectorHealthReport> GetConnectorHealthAsync(string connectorName, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<IReadOnlyList<SyncEvent>> GetRecentSyncsAsync(string? connectorName = null, int limit = 50, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<IReadOnlyList<SyncEvent>> GetFailedSyncsAsync(string? connectorName = null, int limit = 50, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<IReadOnlyList<WebhookEvent>> GetWebhookEventsAsync(string? connectorName = null, int limit = 50, CancellationToken ct = default);
    global::System.Threading.Tasks.Task<IReadOnlyList<CredentialStatus>> GetCredentialStatusesAsync(CancellationToken ct = default);

    void RecordSyncEvent(SyncEvent syncEvent);
    void RecordWebhookEvent(WebhookEvent webhookEvent);
}

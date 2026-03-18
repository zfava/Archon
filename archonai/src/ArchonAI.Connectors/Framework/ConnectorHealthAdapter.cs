using System.Net;
using ArchonAI.Connectors.GoogleWorkspace;
using ArchonAI.Connectors.HubSpot;
using ArchonAI.Connectors.Microsoft365;
using ArchonAI.Connectors.QuickBooks;
using ArchonAI.Connectors.Salesforce;
using ArchonAI.Connectors.Slack;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Connector;

namespace ArchonAI.Connectors.Framework;

/// <summary>
/// Extracts standardized health reports from connector-specific status objects.
/// Uses the adapter pattern to normalize the diverse GetStatus() return types
/// into a consistent ConnectorHealthReport without modifying sealed connector classes.
/// </summary>
public static class ConnectorHealthAdapter
{
    private const int CredentialExpiryWarningMinutes = 60;

    public static ConnectorHealthReport GetHealthReport(IConnector connector)
    {
        return connector switch
        {
            ISalesforceConnector sf => FromSalesforce(sf),
            IHubSpotConnector hs => FromHubSpot(hs),
            ISlackConnector slack => FromSlack(slack),
            IQuickBooksConnector qb => FromQuickBooks(qb),
            IGoogleWorkspaceConnector gws => FromGoogleWorkspace(gws),
            IMicrosoft365Connector m365 => FromMicrosoft365(m365),
            _ => DefaultReport(connector)
        };
    }

    public static async global::System.Threading.Tasks.Task<CredentialStatus> ValidateCredentialsAsync(
        IConnector connector, CancellationToken ct = default)
    {
        try
        {
            return connector switch
            {
                ISalesforceConnector sf => await ValidateSalesforce(sf, ct),
                IHubSpotConnector hs => await ValidateHubSpot(hs, ct),
                ISlackConnector slack => await ValidateSlack(slack, ct),
                IQuickBooksConnector qb => await ValidateQuickBooks(qb, ct),
                IGoogleWorkspaceConnector gws => await ValidateGoogleWorkspace(gws, ct),
                IMicrosoft365Connector m365 => await ValidateMicrosoft365(m365, ct),
                _ => new CredentialStatus(connector.SystemName, false, false, null, false,
                    "Connector does not support credential validation", DateTimeOffset.UtcNow)
            };
        }
        catch (Exception ex)
        {
            return new CredentialStatus(connector.SystemName, true, false, null, false,
                ex.Message, DateTimeOffset.UtcNow);
        }
    }

    public static ConnectorErrorCategory ClassifyHttpError(int statusCode, string? errorBody = null)
    {
        return statusCode switch
        {
            401 => ConnectorErrorCategory.Authentication,
            403 => ConnectorErrorCategory.Authorization,
            404 => ConnectorErrorCategory.NotFound,
            408 => ConnectorErrorCategory.Timeout,
            409 => ConnectorErrorCategory.Conflict,
            429 => ConnectorErrorCategory.RateLimit,
            >= 400 and < 500 => ConnectorErrorCategory.InvalidRequest,
            >= 500 and < 600 => ConnectorErrorCategory.ServerError,
            _ => ConnectorErrorCategory.Unknown
        };
    }

    // ── Salesforce ────────────────────────────────────────────

    private static ConnectorHealthReport FromSalesforce(ISalesforceConnector sf)
    {
        var s = sf.GetStatus();
        return BuildReport("salesforce", s.IsConnected, s.LastAuthenticatedAtUtc, s.TokenExpiresAtUtc,
            s.TotalRequests, s.FailedRequests, s.RateLimitRemaining, s.InstanceUrl);
    }

    private static async global::System.Threading.Tasks.Task<CredentialStatus> ValidateSalesforce(
        ISalesforceConnector sf, CancellationToken ct)
    {
        var result = await sf.AuthenticateAsync(ct);
        var status = sf.GetStatus();
        return new CredentialStatus("salesforce", true, result.IsAuthenticated,
            status.TokenExpiresAtUtc, IsExpiryWarning(status.TokenExpiresAtUtc),
            result.Error, DateTimeOffset.UtcNow);
    }

    // ── HubSpot ───────────────────────────────────────────────

    private static ConnectorHealthReport FromHubSpot(IHubSpotConnector hs)
    {
        var s = hs.GetStatus();
        return BuildReport("hubspot", s.IsConnected, s.LastAuthenticatedAtUtc, s.TokenExpiresAtUtc,
            s.TotalRequests, s.FailedRequests, s.DailyRateLimitRemaining, s.PortalId);
    }

    private static async global::System.Threading.Tasks.Task<CredentialStatus> ValidateHubSpot(
        IHubSpotConnector hs, CancellationToken ct)
    {
        var result = await hs.AuthenticateAsync(ct);
        var status = hs.GetStatus();
        return new CredentialStatus("hubspot", true, result.IsAuthenticated,
            status.TokenExpiresAtUtc, IsExpiryWarning(status.TokenExpiresAtUtc),
            result.Error, DateTimeOffset.UtcNow);
    }

    // ── Slack ─────────────────────────────────────────────────

    private static ConnectorHealthReport FromSlack(ISlackConnector slack)
    {
        var s = slack.GetStatus();
        long totalReqs = s.TotalMessagesSent + s.TotalMessagesRead + s.WebhookEventsProcessed;
        return BuildReport("slack", s.IsConnected, s.LastAuthenticatedAtUtc, null,
            totalReqs, s.FailedRequests, s.RateLimitRemaining, s.TeamName);
    }

    private static async global::System.Threading.Tasks.Task<CredentialStatus> ValidateSlack(
        ISlackConnector slack, CancellationToken ct)
    {
        var result = await slack.AuthenticateAsync(ct);
        return new CredentialStatus("slack", true, result.IsAuthenticated,
            null, false, result.Error, DateTimeOffset.UtcNow);
    }

    // ── QuickBooks ────────────────────────────────────────────

    private static ConnectorHealthReport FromQuickBooks(IQuickBooksConnector qb)
    {
        var s = qb.GetStatus();
        return BuildReport("quickbooks", s.IsConnected, s.LastAuthenticatedAtUtc, s.TokenExpiresAtUtc,
            s.TotalRequests, s.FailedRequests, s.RateLimitRemaining, s.CompanyId);
    }

    private static async global::System.Threading.Tasks.Task<CredentialStatus> ValidateQuickBooks(
        IQuickBooksConnector qb, CancellationToken ct)
    {
        var result = await qb.AuthenticateAsync(ct);
        var status = qb.GetStatus();
        return new CredentialStatus("quickbooks", true, result.IsAuthenticated,
            status.TokenExpiresAtUtc, IsExpiryWarning(status.TokenExpiresAtUtc),
            result.Error, DateTimeOffset.UtcNow);
    }

    // ── Google Workspace ──────────────────────────────────────

    private static ConnectorHealthReport FromGoogleWorkspace(IGoogleWorkspaceConnector gws)
    {
        var s = gws.GetStatus();
        return BuildReport("google-workspace", s.IsConnected, s.LastAuthenticatedAtUtc, s.TokenExpiresAtUtc,
            s.TotalRequests, s.FailedRequests, s.RateLimitRemaining, s.Email);
    }

    private static async global::System.Threading.Tasks.Task<CredentialStatus> ValidateGoogleWorkspace(
        IGoogleWorkspaceConnector gws, CancellationToken ct)
    {
        var emails = await gws.GetEmailsAsync(query: null, maxResults: 1, ct);
        var status = gws.GetStatus();
        return new CredentialStatus("google-workspace", true, status.IsConnected,
            status.TokenExpiresAtUtc, IsExpiryWarning(status.TokenExpiresAtUtc),
            null, DateTimeOffset.UtcNow);
    }

    // ── Microsoft 365 ─────────────────────────────────────────

    private static ConnectorHealthReport FromMicrosoft365(IMicrosoft365Connector m365)
    {
        var s = m365.GetStatus();
        return BuildReport("microsoft365", s.IsConnected, s.LastAuthenticatedAtUtc, s.TokenExpiresAtUtc,
            s.TotalRequests, s.FailedRequests, s.RateLimitRemaining, s.TenantId);
    }

    private static async global::System.Threading.Tasks.Task<CredentialStatus> ValidateMicrosoft365(
        IMicrosoft365Connector m365, CancellationToken ct)
    {
        var emails = await m365.GetEmailsAsync(filter: null, top: 1, ct);
        var status = m365.GetStatus();
        return new CredentialStatus("microsoft365", true, status.IsConnected,
            status.TokenExpiresAtUtc, IsExpiryWarning(status.TokenExpiresAtUtc),
            null, DateTimeOffset.UtcNow);
    }

    // ── Helpers ───────────────────────────────────────────────

    private static ConnectorHealthReport BuildReport(
        string name, bool isConnected, DateTimeOffset? lastAuth, DateTimeOffset? tokenExpiry,
        long totalRequests, long failedRequests, int rateLimitRemaining, string? statusMessage)
    {
        double errorRate = totalRequests > 0 ? (double)failedRequests / totalRequests : 0;
        bool expiryWarning = IsExpiryWarning(tokenExpiry);

        var status = ConnectorHealthStatus.Unknown;
        if (isConnected && errorRate < 0.05 && !expiryWarning)
            status = ConnectorHealthStatus.Healthy;
        else if (isConnected && (errorRate < 0.20 || expiryWarning))
            status = ConnectorHealthStatus.Degraded;
        else if (!isConnected && lastAuth is null)
            status = ConnectorHealthStatus.NotConfigured;
        else if (!isConnected)
            status = ConnectorHealthStatus.Unhealthy;

        return new ConnectorHealthReport(name, status, isConnected, lastAuth, tokenExpiry,
            expiryWarning, totalRequests, failedRequests, errorRate,
            rateLimitRemaining, statusMessage, DateTimeOffset.UtcNow);
    }

    private static ConnectorHealthReport DefaultReport(IConnector connector)
    {
        return new ConnectorHealthReport(connector.SystemName, ConnectorHealthStatus.Unknown,
            false, null, null, false, 0, 0, 0, int.MaxValue,
            "No health reporting available", DateTimeOffset.UtcNow);
    }

    private static bool IsExpiryWarning(DateTimeOffset? expiry) =>
        expiry.HasValue && expiry.Value < DateTimeOffset.UtcNow.AddMinutes(CredentialExpiryWarningMinutes);
}

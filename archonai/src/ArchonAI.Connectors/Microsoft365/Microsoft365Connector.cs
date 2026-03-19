using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArchonAI.Common.Observability;
using ObsTelemetry = ArchonAI.Common.Observability.Telemetry;
using ArchonAI.Connectors.Framework;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Connectors.Microsoft365;

public sealed class Microsoft365Connector : IMicrosoft365Connector, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IEventBus _eventBus;
    private readonly ILogger<Microsoft365Connector> _logger;
    private readonly Microsoft365Options _options;
    private readonly ConnectorShadowMetrics _shadowMetrics;

    private string? _accessToken;
    private string? _tenantId;
    private string? _userPrincipalName;
    private DateTimeOffset? _tokenExpiresAtUtc;
    private DateTimeOffset? _lastAuthenticatedAtUtc;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    private long _totalRequests;
    private long _failedRequests;
    private long _emailsSent;
    private long _teamsMessagesSent;
    private long _sharePointOps;
    private long _oneDriveOps;
    private int _rateLimitRemaining = int.MaxValue;

    public Microsoft365Connector(
        HttpClient httpClient,
        IEventBus eventBus,
        ILogger<Microsoft365Connector> logger,
        IOptions<Microsoft365Options> options,
        ConnectorShadowMetrics? shadowMetrics = null)
    {
        _httpClient = httpClient;
        _eventBus = eventBus;
        _logger = logger;
        _options = options.Value;
        _shadowMetrics = shadowMetrics ?? new ConnectorShadowMetrics();
        _tenantId = string.IsNullOrWhiteSpace(_options.TenantId) ? null : _options.TenantId;
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds);
    }

    public string SystemName => "microsoft365";

    // --- Authentication (Azure AD client_credentials) ---

    public async global::System.Threading.Tasks.Task<M365AuthResult> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        await _authLock.WaitAsync(cancellationToken);
        try
        {
            return await AuthenticateCoreAsync(cancellationToken);
        }
        finally
        {
            _authLock.Release();
        }
    }

    // --- Outlook ---

    public async global::System.Threading.Tasks.Task<IReadOnlyList<OutlookMessage>> GetEmailsAsync(
        string? filter, int top = 20, CancellationToken cancellationToken = default)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("M365.GetEmails");

        await EnsureAuthenticatedAsync(cancellationToken);

        string url = $"{_options.GraphBaseUrl}/me/messages?$top={Math.Clamp(top, 1, 500)}&$orderby=receivedDateTime desc&$select=id,from,subject,bodyPreview,isRead,receivedDateTime";
        if (!string.IsNullOrWhiteSpace(filter))
            url += $"&$filter={Uri.EscapeDataString(filter)}";

        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, url, null, cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var messages = new List<OutlookMessage>();

        if (body.TryGetProperty("value", out var valueArray))
        {
            foreach (var msg in valueArray.EnumerateArray())
            {
                messages.Add(ParseOutlookMessage(msg));
            }
        }

        ObsTelemetry.M365QueryOps.Add(1, new KeyValuePair<string, object?>("service", "outlook"));
        _logger.LogInformation("Outlook query returned {Count} messages", messages.Count);

        return messages;
    }

    public async global::System.Threading.Tasks.Task<OutlookSendResult> SendEmailAsync(
        string to, string subject, string body, bool isHtml = false, CancellationToken cancellationToken = default)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("M365.SendEmail");

        if (string.IsNullOrWhiteSpace(to))
            throw new ArgumentException("Recipient email is required.", nameof(to));
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject is required.", nameof(subject));

        await EnsureAuthenticatedAsync(cancellationToken);

        var payload = new
        {
            message = new
            {
                subject,
                body = new
                {
                    contentType = isHtml ? "HTML" : "Text",
                    content = body
                },
                toRecipients = new[]
                {
                    new { emailAddress = new { address = to } }
                }
            },
            saveToSentItems = true
        };

        string url = $"{_options.GraphBaseUrl}/me/sendMail";
        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Post, url, JsonContent.Create(payload), cancellationToken),
            cancellationToken);

        // sendMail returns 202 Accepted with no body on success
        if (response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK or HttpStatusCode.NoContent)
        {
            Interlocked.Increment(ref _emailsSent);
            ObsTelemetry.M365WriteOps.Add(1,
                new KeyValuePair<string, object?>("service", "outlook"),
                new KeyValuePair<string, object?>("operation", "send_email"));

            _logger.LogInformation("Outlook email sent to {To}", to);
            await EmitAuditEventAsync("m365.outlook.email.sent", "Email", to, cancellationToken);

            return new OutlookSendResult(true, null, null);
        }

        string error = "Email send failed";
        try
        {
            var errBody = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (errBody.TryGetProperty("error", out var errObj) &&
                errObj.TryGetProperty("message", out var errMsg))
            {
                error = errMsg.GetString() ?? error;
            }
        }
        catch { /* ignore parse errors */ }

        return new OutlookSendResult(false, null, error);
    }

    // --- Teams ---

    public async global::System.Threading.Tasks.Task<IReadOnlyList<TeamsChannelInfo>> GetTeamsChannelsAsync(
        string teamId, CancellationToken cancellationToken = default)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("M365.GetTeamsChannels");
        activity?.SetTag("m365.team_id", teamId);

        if (string.IsNullOrWhiteSpace(teamId))
            throw new ArgumentException("Team ID is required.", nameof(teamId));

        await EnsureAuthenticatedAsync(cancellationToken);

        string url = $"{_options.GraphBaseUrl}/teams/{Uri.EscapeDataString(teamId)}/channels";
        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, url, null, cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var channels = new List<TeamsChannelInfo>();

        if (body.TryGetProperty("value", out var valueArray))
        {
            foreach (var ch in valueArray.EnumerateArray())
            {
                string id = ch.TryGetProperty("id", out var idP) ? idP.GetString() ?? string.Empty : string.Empty;
                string displayName = ch.TryGetProperty("displayName", out var dnP) ? dnP.GetString() ?? string.Empty : string.Empty;
                string? description = ch.TryGetProperty("description", out var descP) ? descP.GetString() : null;
                string membershipType = ch.TryGetProperty("membershipType", out var mtP) ? mtP.GetString() ?? "standard" : "standard";

                channels.Add(new TeamsChannelInfo(id, displayName, description, membershipType));
            }
        }

        ObsTelemetry.M365QueryOps.Add(1, new KeyValuePair<string, object?>("service", "teams"));
        _logger.LogInformation("Teams channels for {TeamId}: {Count}", teamId, channels.Count);

        return channels;
    }

    public async global::System.Threading.Tasks.Task<TeamsMessageResult> SendTeamsMessageAsync(
        string teamId, string channelId, string content, CancellationToken cancellationToken = default)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("M365.SendTeamsMessage");

        if (string.IsNullOrWhiteSpace(teamId))
            throw new ArgumentException("Team ID is required.", nameof(teamId));
        if (string.IsNullOrWhiteSpace(channelId))
            throw new ArgumentException("Channel ID is required.", nameof(channelId));
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Message content is required.", nameof(content));

        await EnsureAuthenticatedAsync(cancellationToken);

        var payload = new
        {
            body = new
            {
                content,
                contentType = "text"
            }
        };

        string url = $"{_options.GraphBaseUrl}/teams/{Uri.EscapeDataString(teamId)}/channels/{Uri.EscapeDataString(channelId)}/messages";
        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Post, url, JsonContent.Create(payload), cancellationToken),
            cancellationToken);

        var responseBody = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        string? messageId = responseBody.TryGetProperty("id", out var msgIdP) ? msgIdP.GetString() : null;

        Interlocked.Increment(ref _teamsMessagesSent);
        ObsTelemetry.M365WriteOps.Add(1,
            new KeyValuePair<string, object?>("service", "teams"),
            new KeyValuePair<string, object?>("operation", "send_message"));

        _logger.LogInformation("Teams message sent to {TeamId}/{ChannelId}, messageId={MessageId}", teamId, channelId, messageId);
        await EmitAuditEventAsync("m365.teams.message.sent", "TeamsMessage", messageId ?? string.Empty, cancellationToken);

        return new TeamsMessageResult(true, messageId, null);
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<TeamsMessage>> GetTeamsMessagesAsync(
        string teamId, string channelId, int top = 20, CancellationToken cancellationToken = default)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("M365.GetTeamsMessages");

        if (string.IsNullOrWhiteSpace(teamId))
            throw new ArgumentException("Team ID is required.", nameof(teamId));
        if (string.IsNullOrWhiteSpace(channelId))
            throw new ArgumentException("Channel ID is required.", nameof(channelId));

        await EnsureAuthenticatedAsync(cancellationToken);

        string url = $"{_options.GraphBaseUrl}/teams/{Uri.EscapeDataString(teamId)}/channels/{Uri.EscapeDataString(channelId)}/messages?$top={Math.Clamp(top, 1, 50)}";
        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, url, null, cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var messages = new List<TeamsMessage>();

        if (body.TryGetProperty("value", out var valueArray))
        {
            foreach (var msg in valueArray.EnumerateArray())
            {
                string id = msg.TryGetProperty("id", out var idP) ? idP.GetString() ?? string.Empty : string.Empty;

                string? fromName = null;
                if (msg.TryGetProperty("from", out var fromP) &&
                    fromP.TryGetProperty("user", out var userP) &&
                    userP.TryGetProperty("displayName", out var dnP))
                {
                    fromName = dnP.GetString();
                }

                string bodyContent = string.Empty;
                if (msg.TryGetProperty("body", out var bodyP) &&
                    bodyP.TryGetProperty("content", out var contentP))
                {
                    bodyContent = contentP.GetString() ?? string.Empty;
                }

                var createdAt = msg.TryGetProperty("createdDateTime", out var dtP)
                    && DateTimeOffset.TryParse(dtP.GetString(), out var parsed) ? parsed : DateTimeOffset.UtcNow;

                messages.Add(new TeamsMessage(id, fromName, bodyContent, createdAt));
            }
        }

        ObsTelemetry.M365QueryOps.Add(1, new KeyValuePair<string, object?>("service", "teams"));
        _logger.LogInformation("Teams messages for {TeamId}/{ChannelId}: {Count}", teamId, channelId, messages.Count);

        return messages;
    }

    // --- SharePoint ---

    public async global::System.Threading.Tasks.Task<IReadOnlyList<SharePointItem>> GetSharePointItemsAsync(
        string siteId, string? listId, int top = 50, CancellationToken cancellationToken = default)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("M365.GetSharePointItems");

        if (string.IsNullOrWhiteSpace(siteId))
            throw new ArgumentException("Site ID is required.", nameof(siteId));

        await EnsureAuthenticatedAsync(cancellationToken);

        string url;
        if (!string.IsNullOrWhiteSpace(listId))
        {
            url = $"{_options.GraphBaseUrl}/sites/{Uri.EscapeDataString(siteId)}/lists/{Uri.EscapeDataString(listId)}/items?$top={Math.Clamp(top, 1, 500)}&$expand=fields";
        }
        else
        {
            url = $"{_options.GraphBaseUrl}/sites/{Uri.EscapeDataString(siteId)}/drive/root/children?$top={Math.Clamp(top, 1, 500)}";
        }

        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, url, null, cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var items = new List<SharePointItem>();

        if (body.TryGetProperty("value", out var valueArray))
        {
            foreach (var item in valueArray.EnumerateArray())
            {
                items.Add(ParseSharePointItem(item));
            }
        }

        Interlocked.Increment(ref _sharePointOps);
        ObsTelemetry.M365QueryOps.Add(1, new KeyValuePair<string, object?>("service", "sharepoint"));
        _logger.LogInformation("SharePoint items for site {SiteId}: {Count}", siteId, items.Count);

        return items;
    }

    public async global::System.Threading.Tasks.Task<SharePointItem> GetSharePointItemAsync(
        string siteId, string driveId, string itemId, CancellationToken cancellationToken = default)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("M365.GetSharePointItem");

        if (string.IsNullOrWhiteSpace(siteId))
            throw new ArgumentException("Site ID is required.", nameof(siteId));
        if (string.IsNullOrWhiteSpace(itemId))
            throw new ArgumentException("Item ID is required.", nameof(itemId));

        await EnsureAuthenticatedAsync(cancellationToken);

        string url = string.IsNullOrWhiteSpace(driveId)
            ? $"{_options.GraphBaseUrl}/sites/{Uri.EscapeDataString(siteId)}/drive/items/{Uri.EscapeDataString(itemId)}"
            : $"{_options.GraphBaseUrl}/sites/{Uri.EscapeDataString(siteId)}/drives/{Uri.EscapeDataString(driveId)}/items/{Uri.EscapeDataString(itemId)}";

        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, url, null, cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        Interlocked.Increment(ref _sharePointOps);
        ObsTelemetry.M365QueryOps.Add(1, new KeyValuePair<string, object?>("service", "sharepoint"));

        return ParseSharePointItem(body);
    }

    // --- OneDrive ---

    public async global::System.Threading.Tasks.Task<IReadOnlyList<OneDriveItem>> ListOneDriveFilesAsync(
        string? folderId, int top = 50, CancellationToken cancellationToken = default)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("M365.ListOneDriveFiles");

        await EnsureAuthenticatedAsync(cancellationToken);

        string path = string.IsNullOrWhiteSpace(folderId)
            ? "root/children"
            : $"items/{Uri.EscapeDataString(folderId)}/children";

        string url = $"{_options.GraphBaseUrl}/me/drive/{path}?$top={Math.Clamp(top, 1, 500)}&$select=id,name,file,size,parentReference,webUrl,lastModifiedDateTime";

        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, url, null, cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var items = new List<OneDriveItem>();

        if (body.TryGetProperty("value", out var valueArray))
        {
            foreach (var item in valueArray.EnumerateArray())
            {
                items.Add(ParseOneDriveItem(item));
            }
        }

        Interlocked.Increment(ref _oneDriveOps);
        ObsTelemetry.M365QueryOps.Add(1, new KeyValuePair<string, object?>("service", "onedrive"));
        _logger.LogInformation("OneDrive listed {Count} items", items.Count);

        return items;
    }

    public async global::System.Threading.Tasks.Task<OneDriveItem> GetOneDriveItemAsync(
        string itemId, CancellationToken cancellationToken = default)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("M365.GetOneDriveItem");

        if (string.IsNullOrWhiteSpace(itemId))
            throw new ArgumentException("Item ID is required.", nameof(itemId));

        await EnsureAuthenticatedAsync(cancellationToken);

        string url = $"{_options.GraphBaseUrl}/me/drive/items/{Uri.EscapeDataString(itemId)}?$select=id,name,file,size,parentReference,webUrl,lastModifiedDateTime";

        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, url, null, cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        Interlocked.Increment(ref _oneDriveOps);
        ObsTelemetry.M365QueryOps.Add(1, new KeyValuePair<string, object?>("service", "onedrive"));

        return ParseOneDriveItem(body);
    }

    // --- Status ---

    public M365ConnectorStatus GetStatus()
    {
        return new M365ConnectorStatus(
            IsConnected: _accessToken is not null && (_tokenExpiresAtUtc is null || _tokenExpiresAtUtc > DateTimeOffset.UtcNow),
            TenantId: _tenantId,
            UserPrincipalName: _userPrincipalName,
            LastAuthenticatedAtUtc: _lastAuthenticatedAtUtc,
            TokenExpiresAtUtc: _tokenExpiresAtUtc,
            TotalRequests: Interlocked.Read(ref _totalRequests),
            FailedRequests: Interlocked.Read(ref _failedRequests),
            EmailsSent: Interlocked.Read(ref _emailsSent),
            TeamsMessagesSent: Interlocked.Read(ref _teamsMessagesSent),
            SharePointOps: Interlocked.Read(ref _sharePointOps),
            OneDriveOps: Interlocked.Read(ref _oneDriveOps),
            RateLimitRemaining: _rateLimitRemaining,
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task PushResultAsync(ExecutionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Microsoft 365 connector received execution result for task {TaskId}", result.TaskId);

        await EmitAuditEventAsync("m365.result.pushed", "ExecutionResult", result.TaskId.ToString(), cancellationToken);
    }

    public void Dispose()
    {
        _authLock.Dispose();
    }

    // --- Private helpers ---

    private async global::System.Threading.Tasks.Task<M365AuthResult> AuthenticateCoreAsync(CancellationToken cancellationToken)
    {
        using var activity = ObsTelemetry.ActivitySource.StartActivity("M365.Authenticate");

        try
        {
            ObsTelemetry.M365AuthAttempts.Add(1, new KeyValuePair<string, object?>("result", "attempt"));

            string tokenUrl = _options.OAuthTokenUrl.Replace("{TenantId}", _options.TenantId);

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["scope"] = _options.Scope
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
            {
                Content = form
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string error = body.TryGetProperty("error_description", out var desc)
                    ? desc.GetString() ?? "Unknown error"
                    : body.TryGetProperty("error", out var err)
                        ? err.GetString() ?? "Authentication failed"
                        : "Authentication failed";

                _logger.LogError("Azure AD OAuth failed: {Error}", error);
                ObsTelemetry.M365AuthAttempts.Add(1, new KeyValuePair<string, object?>("result", "failure"));
                Interlocked.Increment(ref _failedRequests);
                return new M365AuthResult(false, null, null, null, error);
            }

            _accessToken = body.GetProperty("access_token").GetString();
            int expiresIn = body.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;

            _lastAuthenticatedAtUtc = DateTimeOffset.UtcNow;
            _tokenExpiresAtUtc = _lastAuthenticatedAtUtc.Value.AddSeconds(expiresIn);
            _tenantId ??= _options.TenantId;

            // Attempt to get user principal name from /me
            if (_userPrincipalName is null)
            {
                _userPrincipalName = await FetchUserPrincipalNameAsync(cancellationToken);
            }

            _logger.LogInformation("Azure AD OAuth succeeded for tenant {TenantId}", _tenantId);
            ObsTelemetry.M365AuthAttempts.Add(1, new KeyValuePair<string, object?>("result", "success"));

            return new M365AuthResult(true, _tenantId, _userPrincipalName, _tokenExpiresAtUtc, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure AD OAuth failed with exception");
            ObsTelemetry.M365AuthAttempts.Add(1, new KeyValuePair<string, object?>("result", "failure"));
            Interlocked.Increment(ref _failedRequests);
            return new M365AuthResult(false, null, null, null, ex.Message);
        }
    }

    private async global::System.Threading.Tasks.Task<string?> FetchUserPrincipalNameAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAuthorizedRequestAsync(
                HttpMethod.Get, $"{_options.GraphBaseUrl}/me?$select=userPrincipalName", null, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
                return body.TryGetProperty("userPrincipalName", out var upn) ? upn.GetString() : null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch user principal name (may be app-only auth)");
        }

        return null;
    }

    private async global::System.Threading.Tasks.Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _tokenExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return;
        }

        var result = await AuthenticateAsync(cancellationToken);
        if (!result.IsAuthenticated)
        {
            throw new InvalidOperationException($"Microsoft 365 authentication failed: {result.Error}");
        }
    }

    private static OutlookMessage ParseOutlookMessage(JsonElement msg)
    {
        string id = msg.TryGetProperty("id", out var idP) ? idP.GetString() ?? string.Empty : string.Empty;

        string? from = null;
        if (msg.TryGetProperty("from", out var fromP) &&
            fromP.TryGetProperty("emailAddress", out var emailAddr) &&
            emailAddr.TryGetProperty("address", out var addrP))
        {
            from = addrP.GetString();
        }

        string? subject = msg.TryGetProperty("subject", out var subP) ? subP.GetString() : null;
        string bodyPreview = msg.TryGetProperty("bodyPreview", out var bpP) ? bpP.GetString() ?? string.Empty : string.Empty;
        bool isRead = msg.TryGetProperty("isRead", out var irP) && irP.GetBoolean();

        var receivedAt = msg.TryGetProperty("receivedDateTime", out var rdP)
            && DateTimeOffset.TryParse(rdP.GetString(), out var parsed) ? parsed : DateTimeOffset.UtcNow;

        return new OutlookMessage(id, from, subject, bodyPreview, isRead, receivedAt);
    }

    private static SharePointItem ParseSharePointItem(JsonElement item)
    {
        string id = item.TryGetProperty("id", out var idP) ? idP.GetString() ?? string.Empty : string.Empty;
        string name = item.TryGetProperty("name", out var nP) ? nP.GetString() ?? string.Empty : string.Empty;
        string? webUrl = item.TryGetProperty("webUrl", out var wP) ? wP.GetString() : null;

        string? contentType = null;
        if (item.TryGetProperty("contentType", out var ctP) && ctP.TryGetProperty("name", out var ctName))
        {
            contentType = ctName.GetString();
        }
        else if (item.TryGetProperty("file", out var fileP) && fileP.TryGetProperty("mimeType", out var mtP))
        {
            contentType = mtP.GetString();
        }

        long? size = item.TryGetProperty("size", out var sP) ? sP.GetInt64() : null;

        DateTimeOffset? modified = item.TryGetProperty("lastModifiedDateTime", out var lmP)
            && DateTimeOffset.TryParse(lmP.GetString(), out var m) ? m : null;

        return new SharePointItem(id, name, webUrl, contentType, size, modified, DateTimeOffset.UtcNow);
    }

    private static OneDriveItem ParseOneDriveItem(JsonElement item)
    {
        string id = item.TryGetProperty("id", out var idP) ? idP.GetString() ?? string.Empty : string.Empty;
        string name = item.TryGetProperty("name", out var nP) ? nP.GetString() ?? string.Empty : string.Empty;

        string? mimeType = null;
        if (item.TryGetProperty("file", out var fileP) && fileP.TryGetProperty("mimeType", out var mtP))
        {
            mimeType = mtP.GetString();
        }

        long? size = item.TryGetProperty("size", out var sP) ? sP.GetInt64() : null;

        string? parentId = null;
        if (item.TryGetProperty("parentReference", out var prP) && prP.TryGetProperty("id", out var prIdP))
        {
            parentId = prIdP.GetString();
        }

        string? webUrl = item.TryGetProperty("webUrl", out var wP) ? wP.GetString() : null;

        DateTimeOffset? modified = item.TryGetProperty("lastModifiedDateTime", out var lmP)
            && DateTimeOffset.TryParse(lmP.GetString(), out var m) ? m : null;

        return new OneDriveItem(id, name, mimeType, size, parentId, webUrl, modified, DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<HttpResponseMessage> SendAuthorizedRequestAsync(
        HttpMethod method, string url, HttpContent? content, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _totalRequests);

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (content is not null)
        {
            request.Content = content;
        }

        var sw = Stopwatch.StartNew();
        var response = await _httpClient.SendAsync(request, cancellationToken);
        sw.Stop();
        UpdateRateLimitInfo(response);

        bool success = response.IsSuccessStatusCode;
        _shadowMetrics.RecordRequest(SystemName, success, sw.Elapsed.TotalMilliseconds);

        if (!success)
        {
            Interlocked.Increment(ref _failedRequests);
            ObsTelemetry.M365Errors.Add(1, new KeyValuePair<string, object?>("status_code", (int)response.StatusCode));

            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Microsoft Graph API returned {StatusCode}: {Body}", response.StatusCode, errorBody);
        }

        return response;
    }

    private async global::System.Threading.Tasks.Task<HttpResponseMessage> ExecuteWithRetryAsync(
        Func<global::System.Threading.Tasks.Task<HttpResponseMessage>> operation,
        CancellationToken cancellationToken)
    {
        int attempt = 0;
        while (true)
        {
            attempt++;
            var response = await operation();

            if (response.IsSuccessStatusCode ||
                response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.NoContent)
            {
                return response;
            }

            bool isRetryable = response.StatusCode is HttpStatusCode.TooManyRequests
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout
                or HttpStatusCode.RequestTimeout;

            if (!isRetryable || attempt >= _options.MaxRetries)
            {
                response.EnsureSuccessStatusCode();
            }

            int delayMs = _options.RetryBaseDelayMs * (1 << (attempt - 1));

            if (response.StatusCode == HttpStatusCode.TooManyRequests &&
                response.Headers.RetryAfter?.Delta is { } retryAfter)
            {
                delayMs = Math.Max(delayMs, (int)retryAfter.TotalMilliseconds);
            }

            _logger.LogWarning(
                "Microsoft Graph request failed with {StatusCode}, retrying in {DelayMs}ms (attempt {Attempt}/{MaxRetries})",
                response.StatusCode, delayMs, attempt, _options.MaxRetries);

            ObsTelemetry.M365Retries.Add(1);

            await global::System.Threading.Tasks.Task.Delay(delayMs, cancellationToken);
        }
    }

    private void UpdateRateLimitInfo(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("RateLimit-Remaining", out var values))
        {
            string? header = values.FirstOrDefault();
            if (header is not null && int.TryParse(header, out int remaining))
            {
                _rateLimitRemaining = remaining;
                ObsTelemetry.M365RateLimitRemaining.Record(remaining);

                if (remaining < 50)
                {
                    _logger.LogWarning("Microsoft Graph rate limit approaching: {Remaining} remaining", remaining);
                }
            }
        }
    }

    private async global::System.Threading.Tasks.Task EmitAuditEventAsync(
        string eventType, string objectType, string recordId, CancellationToken cancellationToken)
    {
        var auditEvent = new SystemEvent(
            Id: Guid.NewGuid(),
            EventType: eventType,
            Source: SystemName,
            CorrelationId: Guid.NewGuid(),
            Payload: new Dictionary<string, string>
            {
                ["objectType"] = objectType,
                ["recordId"] = recordId,
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
            },
            OccurredAtUtc: DateTimeOffset.UtcNow);

        await _eventBus.PublishAsync(auditEvent, cancellationToken);
    }
}

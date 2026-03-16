using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Connectors.GoogleWorkspace;

public sealed class GoogleWorkspaceConnector : IGoogleWorkspaceConnector, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IEventBus _eventBus;
    private readonly ILogger<GoogleWorkspaceConnector> _logger;
    private readonly GoogleWorkspaceOptions _options;

    private string? _accessToken;
    private string? _email;
    private DateTimeOffset? _tokenExpiresAtUtc;
    private DateTimeOffset? _lastAuthenticatedAtUtc;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    private long _totalRequests;
    private long _failedRequests;
    private long _emailsSent;
    private long _docsAccessed;
    private long _sheetsAccessed;
    private long _driveOps;
    private int _rateLimitRemaining = int.MaxValue;

    public GoogleWorkspaceConnector(
        HttpClient httpClient,
        IEventBus eventBus,
        ILogger<GoogleWorkspaceConnector> logger,
        IOptions<GoogleWorkspaceOptions> options)
    {
        _httpClient = httpClient;
        _eventBus = eventBus;
        _logger = logger;
        _options = options.Value;
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds);
    }

    public string SystemName => "google-workspace";

    // --- Authentication ---

    public async global::System.Threading.Tasks.Task<GoogleWorkspaceAuthResult> AuthenticateAsync(CancellationToken cancellationToken = default)
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

    // --- Gmail ---

    public async global::System.Threading.Tasks.Task<IReadOnlyList<GmailMessage>> GetEmailsAsync(
        string? query, int maxResults = 20, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("GoogleWorkspace.GetEmails");

        await EnsureAuthenticatedAsync(cancellationToken);

        string url = $"{_options.GmailBaseUrl}/users/me/messages?maxResults={Math.Clamp(maxResults, 1, 500)}";
        if (!string.IsNullOrWhiteSpace(query))
            url += $"&q={Uri.EscapeDataString(query)}";

        var listResponse = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, url, null, cancellationToken),
            cancellationToken);

        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var messages = new List<GmailMessage>();

        if (listBody.TryGetProperty("messages", out var msgArray))
        {
            foreach (var msgRef in msgArray.EnumerateArray())
            {
                string msgId = msgRef.GetProperty("id").GetString()!;
                string threadId = msgRef.TryGetProperty("threadId", out var tid) ? tid.GetString() ?? string.Empty : string.Empty;

                // Fetch individual message metadata
                string detailUrl = $"{_options.GmailBaseUrl}/users/me/messages/{msgId}?format=metadata&metadataHeaders=From&metadataHeaders=To&metadataHeaders=Subject";
                var detailResponse = await ExecuteWithRetryAsync(
                    () => SendAuthorizedRequestAsync(HttpMethod.Get, detailUrl, null, cancellationToken),
                    cancellationToken);

                var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
                messages.Add(ParseGmailMessage(detail, msgId, threadId));

                if (messages.Count >= maxResults) break;
            }
        }

        Telemetry.GoogleWorkspaceQueryOps.Add(1, new KeyValuePair<string, object?>("service", "gmail"));
        _logger.LogInformation("Gmail query returned {Count} messages", messages.Count);

        return messages;
    }

    public async global::System.Threading.Tasks.Task<GmailSendResult> SendEmailAsync(
        string to, string subject, string body, bool isHtml = false, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("GoogleWorkspace.SendEmail");

        if (string.IsNullOrWhiteSpace(to))
            throw new ArgumentException("Recipient email is required.", nameof(to));
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject is required.", nameof(subject));

        await EnsureAuthenticatedAsync(cancellationToken);

        string contentType = isHtml ? "text/html" : "text/plain";
        string rawMessage = $"To: {to}\r\nSubject: {subject}\r\nContent-Type: {contentType}; charset=utf-8\r\n\r\n{body}";
        string encodedMessage = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawMessage))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var payload = new { raw = encodedMessage };
        string url = $"{_options.GmailBaseUrl}/users/me/messages/send";

        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Post, url, JsonContent.Create(payload), cancellationToken),
            cancellationToken);

        var responseBody = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        string? messageId = responseBody.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        string? threadId = responseBody.TryGetProperty("threadId", out var tidProp) ? tidProp.GetString() : null;

        Interlocked.Increment(ref _emailsSent);
        Telemetry.GoogleWorkspaceWriteOps.Add(1,
            new KeyValuePair<string, object?>("service", "gmail"),
            new KeyValuePair<string, object?>("operation", "send"));

        _logger.LogInformation("Gmail sent email to {To}, messageId={MessageId}", to, messageId);
        await EmitAuditEventAsync("google.gmail.sent", "Email", messageId ?? string.Empty, cancellationToken);

        return new GmailSendResult(true, messageId, threadId, null);
    }

    // --- Google Docs ---

    public async global::System.Threading.Tasks.Task<GoogleDocument> GetDocumentAsync(
        string documentId, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("GoogleWorkspace.GetDocument");
        activity?.SetTag("gws.document_id", documentId);

        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID is required.", nameof(documentId));

        await EnsureAuthenticatedAsync(cancellationToken);

        string url = $"{_options.DocsBaseUrl}/documents/{Uri.EscapeDataString(documentId)}";
        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, url, null, cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        string title = body.TryGetProperty("title", out var titleProp)
            ? titleProp.GetString() ?? string.Empty
            : string.Empty;

        string? bodyText = ExtractDocumentText(body);

        Interlocked.Increment(ref _docsAccessed);
        Telemetry.GoogleWorkspaceQueryOps.Add(1, new KeyValuePair<string, object?>("service", "docs"));
        _logger.LogInformation("Retrieved Google Doc {DocumentId}: {Title}", documentId, title);

        return new GoogleDocument(documentId, title, bodyText, DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task<string> CreateDocumentAsync(
        string title, string? content, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("GoogleWorkspace.CreateDocument");

        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Document title is required.", nameof(title));

        await EnsureAuthenticatedAsync(cancellationToken);

        var payload = new { title };
        string url = $"{_options.DocsBaseUrl}/documents";

        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Post, url, JsonContent.Create(payload), cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        string documentId = body.TryGetProperty("documentId", out var idProp)
            ? idProp.GetString() ?? string.Empty
            : string.Empty;

        // If content provided, insert text via batchUpdate
        if (!string.IsNullOrWhiteSpace(content) && !string.IsNullOrWhiteSpace(documentId))
        {
            var batchPayload = new
            {
                requests = new[]
                {
                    new
                    {
                        insertText = new
                        {
                            location = new { index = 1 },
                            text = content
                        }
                    }
                }
            };

            string batchUrl = $"{_options.DocsBaseUrl}/documents/{documentId}:batchUpdate";
            await ExecuteWithRetryAsync(
                () => SendAuthorizedRequestAsync(HttpMethod.Post, batchUrl, JsonContent.Create(batchPayload), cancellationToken),
                cancellationToken);
        }

        Interlocked.Increment(ref _docsAccessed);
        Telemetry.GoogleWorkspaceWriteOps.Add(1,
            new KeyValuePair<string, object?>("service", "docs"),
            new KeyValuePair<string, object?>("operation", "create"));

        _logger.LogInformation("Created Google Doc {DocumentId}: {Title}", documentId, title);
        await EmitAuditEventAsync("google.docs.created", "Document", documentId, cancellationToken);

        return documentId;
    }

    // --- Google Sheets ---

    public async global::System.Threading.Tasks.Task<GoogleSheetData> ReadSpreadsheetAsync(
        string spreadsheetId, string range, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("GoogleWorkspace.ReadSpreadsheet");
        activity?.SetTag("gws.spreadsheet_id", spreadsheetId);

        if (string.IsNullOrWhiteSpace(spreadsheetId))
            throw new ArgumentException("Spreadsheet ID is required.", nameof(spreadsheetId));
        if (string.IsNullOrWhiteSpace(range))
            throw new ArgumentException("Range is required.", nameof(range));

        await EnsureAuthenticatedAsync(cancellationToken);

        string url = $"{_options.SheetsBaseUrl}/spreadsheets/{Uri.EscapeDataString(spreadsheetId)}/values/{Uri.EscapeDataString(range)}";
        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, url, null, cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        string actualRange = body.TryGetProperty("range", out var rangeProp)
            ? rangeProp.GetString() ?? range
            : range;

        var values = new List<IReadOnlyList<string>>();
        if (body.TryGetProperty("values", out var valuesArray))
        {
            foreach (var row in valuesArray.EnumerateArray())
            {
                var rowValues = new List<string>();
                foreach (var cell in row.EnumerateArray())
                {
                    rowValues.Add(cell.GetString() ?? string.Empty);
                }
                values.Add(rowValues);
            }
        }

        int rowCount = values.Count;
        int columnCount = values.Count > 0 ? values.Max(r => r.Count) : 0;

        Interlocked.Increment(ref _sheetsAccessed);
        Telemetry.GoogleWorkspaceQueryOps.Add(1, new KeyValuePair<string, object?>("service", "sheets"));
        _logger.LogInformation("Read Google Sheet {SpreadsheetId} range {Range}: {Rows}x{Cols}",
            spreadsheetId, actualRange, rowCount, columnCount);

        return new GoogleSheetData(spreadsheetId, actualRange, values, rowCount, columnCount, DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task<int> WriteSpreadsheetAsync(
        string spreadsheetId, string range, IReadOnlyList<IReadOnlyList<string>> values,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("GoogleWorkspace.WriteSpreadsheet");
        activity?.SetTag("gws.spreadsheet_id", spreadsheetId);

        if (string.IsNullOrWhiteSpace(spreadsheetId))
            throw new ArgumentException("Spreadsheet ID is required.", nameof(spreadsheetId));
        if (string.IsNullOrWhiteSpace(range))
            throw new ArgumentException("Range is required.", nameof(range));
        if (values is null || values.Count == 0)
            throw new ArgumentException("Values must contain at least one row.", nameof(values));

        await EnsureAuthenticatedAsync(cancellationToken);

        var payload = new
        {
            range,
            majorDimension = "ROWS",
            values
        };

        string url = $"{_options.SheetsBaseUrl}/spreadsheets/{Uri.EscapeDataString(spreadsheetId)}/values/{Uri.EscapeDataString(range)}?valueInputOption=USER_ENTERED";
        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Put, url, JsonContent.Create(payload), cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        int updatedCells = body.TryGetProperty("updatedCells", out var cellsProp) ? cellsProp.GetInt32() : 0;

        Interlocked.Increment(ref _sheetsAccessed);
        Telemetry.GoogleWorkspaceWriteOps.Add(1,
            new KeyValuePair<string, object?>("service", "sheets"),
            new KeyValuePair<string, object?>("operation", "write"));

        _logger.LogInformation("Wrote {UpdatedCells} cells to Google Sheet {SpreadsheetId} range {Range}",
            updatedCells, spreadsheetId, range);
        await EmitAuditEventAsync("google.sheets.updated", "Spreadsheet", spreadsheetId, cancellationToken);

        return updatedCells;
    }

    // --- Google Drive ---

    public async global::System.Threading.Tasks.Task<IReadOnlyList<DriveFileInfo>> ListFilesAsync(
        string? query, int maxResults = 50, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("GoogleWorkspace.ListFiles");

        await EnsureAuthenticatedAsync(cancellationToken);

        string url = $"{_options.DriveBaseUrl}/files?pageSize={Math.Clamp(maxResults, 1, 1000)}&fields=files(id,name,mimeType,size,parents,modifiedTime)";
        if (!string.IsNullOrWhiteSpace(query))
            url += $"&q={Uri.EscapeDataString(query)}";

        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, url, null, cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var files = new List<DriveFileInfo>();

        if (body.TryGetProperty("files", out var fileArray))
        {
            foreach (var file in fileArray.EnumerateArray())
            {
                files.Add(ParseDriveFile(file));
            }
        }

        Interlocked.Increment(ref _driveOps);
        Telemetry.GoogleWorkspaceQueryOps.Add(1, new KeyValuePair<string, object?>("service", "drive"));
        _logger.LogInformation("Google Drive list returned {Count} files", files.Count);

        return files;
    }

    public async global::System.Threading.Tasks.Task<DriveFileInfo> GetFileMetadataAsync(
        string fileId, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("GoogleWorkspace.GetFileMetadata");

        if (string.IsNullOrWhiteSpace(fileId))
            throw new ArgumentException("File ID is required.", nameof(fileId));

        await EnsureAuthenticatedAsync(cancellationToken);

        string url = $"{_options.DriveBaseUrl}/files/{Uri.EscapeDataString(fileId)}?fields=id,name,mimeType,size,parents,modifiedTime";
        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, url, null, cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        Interlocked.Increment(ref _driveOps);
        Telemetry.GoogleWorkspaceQueryOps.Add(1, new KeyValuePair<string, object?>("service", "drive"));

        return ParseDriveFile(body);
    }

    // --- Status ---

    public GoogleWorkspaceConnectorStatus GetStatus()
    {
        return new GoogleWorkspaceConnectorStatus(
            IsConnected: _accessToken is not null && (_tokenExpiresAtUtc is null || _tokenExpiresAtUtc > DateTimeOffset.UtcNow),
            Email: _email,
            LastAuthenticatedAtUtc: _lastAuthenticatedAtUtc,
            TokenExpiresAtUtc: _tokenExpiresAtUtc,
            TotalRequests: Interlocked.Read(ref _totalRequests),
            FailedRequests: Interlocked.Read(ref _failedRequests),
            EmailsSent: Interlocked.Read(ref _emailsSent),
            DocsAccessed: Interlocked.Read(ref _docsAccessed),
            SheetsAccessed: Interlocked.Read(ref _sheetsAccessed),
            DriveOps: Interlocked.Read(ref _driveOps),
            RateLimitRemaining: _rateLimitRemaining,
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task PushResultAsync(ExecutionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Google Workspace connector received execution result for task {TaskId}", result.TaskId);

        await EmitAuditEventAsync("google.workspace.result.pushed", "ExecutionResult", result.TaskId.ToString(), cancellationToken);
    }

    public void Dispose()
    {
        _authLock.Dispose();
    }

    // --- Private helpers ---

    private async global::System.Threading.Tasks.Task<GoogleWorkspaceAuthResult> AuthenticateCoreAsync(CancellationToken cancellationToken)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("GoogleWorkspace.Authenticate");

        try
        {
            Telemetry.GoogleWorkspaceAuthAttempts.Add(1, new KeyValuePair<string, object?>("result", "attempt"));

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _options.RefreshToken,
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.OAuthTokenUrl)
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

                _logger.LogError("Google OAuth failed: {Error}", error);
                Telemetry.GoogleWorkspaceAuthAttempts.Add(1, new KeyValuePair<string, object?>("result", "failure"));
                Interlocked.Increment(ref _failedRequests);
                return new GoogleWorkspaceAuthResult(false, null, null, error);
            }

            _accessToken = body.GetProperty("access_token").GetString();

            int expiresIn = body.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;

            _lastAuthenticatedAtUtc = DateTimeOffset.UtcNow;
            _tokenExpiresAtUtc = _lastAuthenticatedAtUtc.Value.AddSeconds(expiresIn);

            // Retrieve user email from userinfo
            if (_email is null)
            {
                _email = await FetchUserEmailAsync(cancellationToken);
            }

            _logger.LogInformation("Google OAuth succeeded for {Email}", _email);
            Telemetry.GoogleWorkspaceAuthAttempts.Add(1, new KeyValuePair<string, object?>("result", "success"));

            return new GoogleWorkspaceAuthResult(true, _email, _tokenExpiresAtUtc, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google OAuth failed with exception");
            Telemetry.GoogleWorkspaceAuthAttempts.Add(1, new KeyValuePair<string, object?>("result", "failure"));
            Interlocked.Increment(ref _failedRequests);
            return new GoogleWorkspaceAuthResult(false, null, null, ex.Message);
        }
    }

    private async global::System.Threading.Tasks.Task<string?> FetchUserEmailAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAuthorizedRequestAsync(
                HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo", null, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
                return body.TryGetProperty("email", out var emailProp) ? emailProp.GetString() : null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Google user email");
        }

        return _options.ImpersonateUser.Length > 0 ? _options.ImpersonateUser : null;
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
            throw new InvalidOperationException($"Google Workspace authentication failed: {result.Error}");
        }
    }

    private static GmailMessage ParseGmailMessage(JsonElement detail, string msgId, string threadId)
    {
        string? from = null;
        string? to = null;
        string? subject = null;
        string snippet = detail.TryGetProperty("snippet", out var snipProp) ? snipProp.GetString() ?? string.Empty : string.Empty;

        long internalDate = detail.TryGetProperty("internalDate", out var dateProp) && long.TryParse(dateProp.GetString(), out var d) ? d : 0;
        var receivedAt = internalDate > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(internalDate)
            : DateTimeOffset.UtcNow;

        if (detail.TryGetProperty("payload", out var payload) &&
            payload.TryGetProperty("headers", out var headers))
        {
            foreach (var header in headers.EnumerateArray())
            {
                string name = header.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                string value = header.TryGetProperty("value", out var v) ? v.GetString() ?? string.Empty : string.Empty;

                switch (name)
                {
                    case "From": from = value; break;
                    case "To": to = value; break;
                    case "Subject": subject = value; break;
                }
            }
        }

        return new GmailMessage(msgId, threadId, from, to, subject, snippet, receivedAt);
    }

    private static string? ExtractDocumentText(JsonElement docBody)
    {
        if (!docBody.TryGetProperty("body", out var body) ||
            !body.TryGetProperty("content", out var content))
            return null;

        var sb = new StringBuilder();
        foreach (var element in content.EnumerateArray())
        {
            if (element.TryGetProperty("paragraph", out var para) &&
                para.TryGetProperty("elements", out var elements))
            {
                foreach (var elem in elements.EnumerateArray())
                {
                    if (elem.TryGetProperty("textRun", out var textRun) &&
                        textRun.TryGetProperty("content", out var textContent))
                    {
                        sb.Append(textContent.GetString());
                    }
                }
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static DriveFileInfo ParseDriveFile(JsonElement file)
    {
        string id = file.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
        string name = file.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
        string mimeType = file.TryGetProperty("mimeType", out var mimeProp) ? mimeProp.GetString() ?? string.Empty : string.Empty;

        long? size = file.TryGetProperty("size", out var sizeProp) && long.TryParse(sizeProp.GetString(), out var s) ? s : null;

        string? parentId = null;
        if (file.TryGetProperty("parents", out var parents) && parents.GetArrayLength() > 0)
        {
            parentId = parents[0].GetString();
        }

        DateTimeOffset? modifiedAt = file.TryGetProperty("modifiedTime", out var modProp)
            && DateTimeOffset.TryParse(modProp.GetString(), out var m) ? m : null;

        return new DriveFileInfo(id, name, mimeType, size, parentId, modifiedAt, DateTimeOffset.UtcNow);
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

        var response = await _httpClient.SendAsync(request, cancellationToken);
        UpdateRateLimitInfo(response);

        if (!response.IsSuccessStatusCode)
        {
            Interlocked.Increment(ref _failedRequests);
            Telemetry.GoogleWorkspaceErrors.Add(1, new KeyValuePair<string, object?>("status_code", (int)response.StatusCode));

            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Google API returned {StatusCode}: {Body}", response.StatusCode, errorBody);
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

            if (response.IsSuccessStatusCode)
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
                "Google API request failed with {StatusCode}, retrying in {DelayMs}ms (attempt {Attempt}/{MaxRetries})",
                response.StatusCode, delayMs, attempt, _options.MaxRetries);

            Telemetry.GoogleWorkspaceRetries.Add(1);

            await global::System.Threading.Tasks.Task.Delay(delayMs, cancellationToken);
        }
    }

    private void UpdateRateLimitInfo(HttpResponseMessage response)
    {
        // Google APIs use X-RateLimit-Remaining or custom quota headers
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var values))
        {
            string? header = values.FirstOrDefault();
            if (header is not null && int.TryParse(header, out int remaining))
            {
                _rateLimitRemaining = remaining;
                Telemetry.GoogleWorkspaceRateLimitRemaining.Record(remaining);

                if (remaining < 50)
                {
                    _logger.LogWarning("Google API rate limit approaching: {Remaining} remaining", remaining);
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

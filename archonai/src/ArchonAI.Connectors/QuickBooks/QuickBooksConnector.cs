using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArchonAI.Common.Observability;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Connectors.QuickBooks;

public sealed class QuickBooksConnector : IQuickBooksConnector, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IEventBus _eventBus;
    private readonly ILogger<QuickBooksConnector> _logger;
    private readonly QuickBooksOptions _options;

    private string? _accessToken;
    private string? _companyId;
    private DateTimeOffset? _tokenExpiresAtUtc;
    private DateTimeOffset? _lastAuthenticatedAtUtc;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    private long _totalRequests;
    private long _failedRequests;
    private int _rateLimitRemaining = int.MaxValue;

    public QuickBooksConnector(
        HttpClient httpClient,
        IEventBus eventBus,
        ILogger<QuickBooksConnector> logger,
        IOptions<QuickBooksOptions> options)
    {
        _httpClient = httpClient;
        _eventBus = eventBus;
        _logger = logger;
        _options = options.Value;
        _companyId = string.IsNullOrWhiteSpace(_options.CompanyId) ? null : _options.CompanyId;
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds);
    }

    public string SystemName => "quickbooks";

    public async global::System.Threading.Tasks.Task<QuickBooksAuthResult> AuthenticateAsync(CancellationToken cancellationToken = default)
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

    public async global::System.Threading.Tasks.Task<IReadOnlyList<QuickBooksRecord>> GetFinancialReportsAsync(
        string reportType, string? startDate, string? endDate, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("QuickBooks.GetFinancialReports");
        activity?.SetTag("qb.report_type", reportType);

        await EnsureAuthenticatedAsync(cancellationToken);

        string url = $"{BaseCompanyUrl()}/reports/{Uri.EscapeDataString(reportType)}?minorversion=65";
        if (!string.IsNullOrWhiteSpace(startDate))
            url += $"&start_date={Uri.EscapeDataString(startDate)}";
        if (!string.IsNullOrWhiteSpace(endDate))
            url += $"&end_date={Uri.EscapeDataString(endDate)}";

        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, url, null, cancellationToken),
            cancellationToken);

        var records = await ParseReportResponseAsync(reportType, response, cancellationToken);

        _logger.LogInformation("QuickBooks report {ReportType} returned {Count} rows", reportType, records.Count);
        Telemetry.QuickBooksQueryOps.Add(1, new KeyValuePair<string, object?>("object_type", reportType));

        return records;
    }

    public async global::System.Threading.Tasks.Task<string> CreateInvoiceAsync(
        string customerId, IReadOnlyList<QuickBooksLineItem> lineItems, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("QuickBooks.CreateInvoice");
        activity?.SetTag("qb.customer_id", customerId);

        ValidateInvoiceData(customerId, lineItems);
        await EnsureAuthenticatedAsync(cancellationToken);

        string url = $"{BaseCompanyUrl()}/invoice?minorversion=65";

        var invoicePayload = new
        {
            CustomerRef = new { value = customerId },
            Line = lineItems.Select(li => new
            {
                Amount = li.Amount * li.Quantity,
                DetailType = "SalesItemLineDetail",
                SalesItemLineDetail = new
                {
                    ItemRef = li.ItemRef is not null ? new { value = li.ItemRef } : (object?)null,
                    Qty = li.Quantity,
                    UnitPrice = li.Amount
                },
                Description = li.Description
            }).ToArray()
        };

        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Post, url, JsonContent.Create(invoicePayload), cancellationToken),
            cancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        string invoiceId = body.TryGetProperty("Invoice", out var inv) && inv.TryGetProperty("Id", out var id)
            ? id.GetString() ?? string.Empty
            : string.Empty;

        _logger.LogInformation("Created QuickBooks invoice {InvoiceId} for customer {CustomerId}", invoiceId, customerId);
        Telemetry.QuickBooksWriteOps.Add(1,
            new KeyValuePair<string, object?>("operation", "create_invoice"),
            new KeyValuePair<string, object?>("object_type", "Invoice"));

        await EmitAuditEventAsync("quickbooks.invoice.created", "Invoice", invoiceId, cancellationToken);

        return invoiceId;
    }

    public async global::System.Threading.Tasks.Task<string> UpdateCustomerAsync(
        string customerId, IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("QuickBooks.UpdateCustomer");
        activity?.SetTag("qb.customer_id", customerId);

        if (string.IsNullOrWhiteSpace(customerId))
            throw new ArgumentException("Customer ID is required.", nameof(customerId));

        await EnsureAuthenticatedAsync(cancellationToken);

        // QuickBooks requires a sparse update with SyncToken — first read the current record
        string readUrl = $"{BaseCompanyUrl()}/customer/{Uri.EscapeDataString(customerId)}?minorversion=65";
        var readResponse = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, readUrl, null, cancellationToken),
            cancellationToken);

        var readBody = await readResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        string syncToken = readBody.TryGetProperty("Customer", out var cust) && cust.TryGetProperty("SyncToken", out var st)
            ? st.GetString() ?? "0"
            : "0";

        var updatePayload = new Dictionary<string, object>
        {
            ["Id"] = customerId,
            ["SyncToken"] = syncToken,
            ["sparse"] = true
        };
        foreach (var (key, value) in fields)
        {
            updatePayload[key] = value;
        }

        string updateUrl = $"{BaseCompanyUrl()}/customer?minorversion=65";
        await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Post, updateUrl, JsonContent.Create(updatePayload), cancellationToken),
            cancellationToken);

        _logger.LogInformation("Updated QuickBooks customer {CustomerId}", customerId);
        Telemetry.QuickBooksWriteOps.Add(1,
            new KeyValuePair<string, object?>("operation", "update_customer"),
            new KeyValuePair<string, object?>("object_type", "Customer"));

        await EmitAuditEventAsync("quickbooks.customer.updated", "Customer", customerId, cancellationToken);

        return customerId;
    }

    public async global::System.Threading.Tasks.Task<IReadOnlyList<QuickBooksRecord>> GetTransactionHistoryAsync(
        string? accountId, string? startDate, string? endDate, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("QuickBooks.GetTransactionHistory");

        await EnsureAuthenticatedAsync(cancellationToken);

        string query = "SELECT * FROM Purchase";
        var conditions = new List<string>();

        if (!string.IsNullOrWhiteSpace(accountId))
            conditions.Add($"AccountRef = '{accountId}'");
        if (!string.IsNullOrWhiteSpace(startDate))
            conditions.Add($"TxnDate >= '{startDate}'");
        if (!string.IsNullOrWhiteSpace(endDate))
            conditions.Add($"TxnDate <= '{endDate}'");

        if (conditions.Count > 0)
            query += " WHERE " + string.Join(" AND ", conditions);

        query += $" MAXRESULTS {Math.Clamp(limit, 1, 1000)}";

        string url = $"{BaseCompanyUrl()}/query?query={Uri.EscapeDataString(query)}&minorversion=65";

        var response = await ExecuteWithRetryAsync(
            () => SendAuthorizedRequestAsync(HttpMethod.Get, url, null, cancellationToken),
            cancellationToken);

        var records = await ParseQueryResponseAsync("Purchase", response, cancellationToken);

        _logger.LogInformation("QuickBooks transaction query returned {Count} records", records.Count);
        Telemetry.QuickBooksQueryOps.Add(1, new KeyValuePair<string, object?>("object_type", "Purchase"));

        return records;
    }

    public QuickBooksConnectorStatus GetStatus()
    {
        return new QuickBooksConnectorStatus(
            IsConnected: _accessToken is not null && (_tokenExpiresAtUtc is null || _tokenExpiresAtUtc > DateTimeOffset.UtcNow),
            CompanyId: _companyId,
            LastAuthenticatedAtUtc: _lastAuthenticatedAtUtc,
            TokenExpiresAtUtc: _tokenExpiresAtUtc,
            TotalRequests: Interlocked.Read(ref _totalRequests),
            FailedRequests: Interlocked.Read(ref _failedRequests),
            RateLimitRemaining: _rateLimitRemaining,
            StatusAsOfUtc: DateTimeOffset.UtcNow);
    }

    public async global::System.Threading.Tasks.Task PushResultAsync(ExecutionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("QuickBooks connector received execution result for task {TaskId}", result.TaskId);

        await EmitAuditEventAsync("quickbooks.result.pushed", "ExecutionResult", result.TaskId.ToString(), cancellationToken);
    }

    public void Dispose()
    {
        _authLock.Dispose();
    }

    // --- Private helpers ---

    private string BaseCompanyUrl() =>
        $"{_options.BaseUrl}/{_options.ApiVersion}/company/{_companyId}";

    private static void ValidateInvoiceData(string customerId, IReadOnlyList<QuickBooksLineItem> lineItems)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            throw new ArgumentException("Customer ID is required for invoice creation.", nameof(customerId));

        if (lineItems is null || lineItems.Count == 0)
            throw new ArgumentException("At least one line item is required.", nameof(lineItems));

        for (int i = 0; i < lineItems.Count; i++)
        {
            if (lineItems[i].Amount < 0)
                throw new ArgumentException($"Line item {i} has a negative amount.", nameof(lineItems));
            if (lineItems[i].Quantity <= 0)
                throw new ArgumentException($"Line item {i} has a non-positive quantity.", nameof(lineItems));
        }
    }

    private async global::System.Threading.Tasks.Task<QuickBooksAuthResult> AuthenticateCoreAsync(CancellationToken cancellationToken)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("QuickBooks.Authenticate");

        try
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _options.RefreshToken
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.OAuthTokenUrl)
            {
                Content = form
            };

            // QuickBooks OAuth uses Basic auth with client_id:client_secret
            string credentials = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string error = body.TryGetProperty("error_description", out var desc)
                    ? desc.GetString() ?? "Unknown error"
                    : body.TryGetProperty("error", out var err)
                        ? err.GetString() ?? "Authentication failed"
                        : "Authentication failed";

                _logger.LogError("QuickBooks OAuth failed: {Error}", error);
                Interlocked.Increment(ref _failedRequests);
                return new QuickBooksAuthResult(false, null, null, error);
            }

            _accessToken = body.GetProperty("access_token").GetString();

            int expiresIn = body.TryGetProperty("expires_in", out var exp)
                ? exp.GetInt32()
                : 3600;

            _lastAuthenticatedAtUtc = DateTimeOffset.UtcNow;
            _tokenExpiresAtUtc = _lastAuthenticatedAtUtc.Value.AddSeconds(expiresIn);

            if (string.IsNullOrWhiteSpace(_companyId))
            {
                _companyId = _options.CompanyId;
            }

            _logger.LogInformation("QuickBooks OAuth succeeded for company {CompanyId}", _companyId);
            Telemetry.QuickBooksAuthAttempts.Add(1, new KeyValuePair<string, object?>("result", "success"));

            return new QuickBooksAuthResult(true, _companyId, _tokenExpiresAtUtc, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QuickBooks OAuth failed with exception");
            Telemetry.QuickBooksAuthAttempts.Add(1, new KeyValuePair<string, object?>("result", "failure"));
            Interlocked.Increment(ref _failedRequests);
            return new QuickBooksAuthResult(false, null, null, ex.Message);
        }
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
            throw new InvalidOperationException($"QuickBooks authentication failed: {result.Error}");
        }
    }

    private async global::System.Threading.Tasks.Task<IReadOnlyList<QuickBooksRecord>> ParseReportResponseAsync(
        string reportType, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var records = new List<QuickBooksRecord>();

        // QuickBooks reports return Columns + Rows structure
        if (body.TryGetProperty("Rows", out var rows) && rows.TryGetProperty("Row", out var rowArray))
        {
            var columns = new List<string>();
            if (body.TryGetProperty("Columns", out var cols) && cols.TryGetProperty("Column", out var colArray))
            {
                foreach (var col in colArray.EnumerateArray())
                {
                    columns.Add(col.TryGetProperty("ColTitle", out var title)
                        ? title.GetString() ?? string.Empty
                        : string.Empty);
                }
            }

            int rowIndex = 0;
            foreach (var row in rowArray.EnumerateArray())
            {
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (row.TryGetProperty("ColData", out var colData))
                {
                    int colIndex = 0;
                    foreach (var cell in colData.EnumerateArray())
                    {
                        string key = colIndex < columns.Count ? columns[colIndex] : $"col_{colIndex}";
                        string value = cell.TryGetProperty("value", out var v)
                            ? v.GetString() ?? string.Empty
                            : string.Empty;
                        fields[key] = value;
                        colIndex++;
                    }
                }

                records.Add(new QuickBooksRecord(rowIndex.ToString(), reportType, fields, DateTimeOffset.UtcNow));
                rowIndex++;
            }
        }

        return records;
    }

    private async global::System.Threading.Tasks.Task<IReadOnlyList<QuickBooksRecord>> ParseQueryResponseAsync(
        string objectType, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var records = new List<QuickBooksRecord>();

        if (body.TryGetProperty("QueryResponse", out var qr) && qr.TryGetProperty(objectType, out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                string id = item.TryGetProperty("Id", out var idProp)
                    ? idProp.GetString() ?? string.Empty
                    : string.Empty;

                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in item.EnumerateObject())
                {
                    if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        continue;

                    fields[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? string.Empty
                        : prop.Value.ToString();
                }

                records.Add(new QuickBooksRecord(id, objectType, fields, DateTimeOffset.UtcNow));
            }
        }

        return records;
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
            Telemetry.QuickBooksErrors.Add(1, new KeyValuePair<string, object?>("status_code", (int)response.StatusCode));

            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("QuickBooks API returned {StatusCode}: {Body}", response.StatusCode, errorBody);
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
                "QuickBooks request failed with {StatusCode}, retrying in {DelayMs}ms (attempt {Attempt}/{MaxRetries})",
                response.StatusCode, delayMs, attempt, _options.MaxRetries);

            Telemetry.QuickBooksRetries.Add(1);

            await global::System.Threading.Tasks.Task.Delay(delayMs, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await EnsureAuthenticatedAsync(cancellationToken);
            }
        }
    }

    private void UpdateRateLimitInfo(HttpResponseMessage response)
    {
        // QuickBooks uses X-RateLimit-Remaining header
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var values))
        {
            string? header = values.FirstOrDefault();
            if (header is not null && int.TryParse(header, out int remaining))
            {
                _rateLimitRemaining = remaining;
                Telemetry.QuickBooksRateLimitRemaining.Record(remaining);

                if (remaining < 50)
                {
                    _logger.LogWarning("QuickBooks rate limit approaching: {Remaining} remaining", remaining);
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

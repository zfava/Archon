namespace ArchonAI.Core.Interfaces;

public interface IQuickBooksConnector : IConnector
{
    global::System.Threading.Tasks.Task<QuickBooksAuthResult> AuthenticateAsync(CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<QuickBooksRecord>> GetFinancialReportsAsync(
        string reportType,
        string? startDate,
        string? endDate,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<string> CreateInvoiceAsync(
        string customerId,
        IReadOnlyList<QuickBooksLineItem> lineItems,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<string> UpdateCustomerAsync(
        string customerId,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<QuickBooksRecord>> GetTransactionHistoryAsync(
        string? accountId,
        string? startDate,
        string? endDate,
        int limit = 100,
        CancellationToken cancellationToken = default);

    QuickBooksConnectorStatus GetStatus();
}

public sealed record QuickBooksAuthResult(
    bool IsAuthenticated,
    string? CompanyId,
    DateTimeOffset? ExpiresAtUtc,
    string? Error);

public sealed record QuickBooksRecord(
    string Id,
    string ObjectType,
    IReadOnlyDictionary<string, string> Fields,
    DateTimeOffset RetrievedAtUtc);

public sealed record QuickBooksLineItem(
    string Description,
    decimal Amount,
    decimal Quantity,
    string? ItemRef);

public sealed record QuickBooksConnectorStatus(
    bool IsConnected,
    string? CompanyId,
    DateTimeOffset? LastAuthenticatedAtUtc,
    DateTimeOffset? TokenExpiresAtUtc,
    long TotalRequests,
    long FailedRequests,
    int RateLimitRemaining,
    DateTimeOffset StatusAsOfUtc);

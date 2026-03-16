namespace ArchonAI.Core.Interfaces;

public interface ISalesforceConnector : IConnector
{
    global::System.Threading.Tasks.Task<SalesforceAuthResult> AuthenticateAsync(CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<SalesforceRecord>> QueryAccountsAsync(
        string soqlFilter,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<SalesforceRecord>> QueryContactsAsync(
        string soqlFilter,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<SalesforceRecord>> QueryOpportunitiesAsync(
        string soqlFilter,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<string> CreateRecordAsync(
        string objectType,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<string> UpdateRecordAsync(
        string objectType,
        string recordId,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default);

    SalesforceConnectorStatus GetStatus();
}

public sealed record SalesforceAuthResult(bool IsAuthenticated, string? InstanceUrl, DateTimeOffset? ExpiresAtUtc, string? Error);

public sealed record SalesforceRecord(string Id, string ObjectType, IReadOnlyDictionary<string, string> Fields, DateTimeOffset RetrievedAtUtc);

public sealed record SalesforceConnectorStatus(
    bool IsConnected,
    string? InstanceUrl,
    DateTimeOffset? LastAuthenticatedAtUtc,
    DateTimeOffset? TokenExpiresAtUtc,
    long TotalRequests,
    long FailedRequests,
    int RateLimitRemaining,
    DateTimeOffset StatusAsOfUtc);

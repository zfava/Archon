namespace ArchonAI.Core.Interfaces;

public interface IHubSpotConnector : IConnector
{
    global::System.Threading.Tasks.Task<HubSpotAuthResult> AuthenticateAsync(CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<HubSpotRecord>> GetContactsAsync(
        string? filter,
        int limit = 100,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<HubSpotRecord>> GetDealsAsync(
        string? filter,
        int limit = 100,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<string> UpdatePipelineRecordAsync(
        string objectType,
        string recordId,
        IReadOnlyDictionary<string, string> properties,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<string> CreateRecordAsync(
        string objectType,
        IReadOnlyDictionary<string, string> properties,
        CancellationToken cancellationToken = default);

    HubSpotConnectorStatus GetStatus();
}

public sealed record HubSpotAuthResult(
    bool IsAuthenticated,
    string? PortalId,
    DateTimeOffset? ExpiresAtUtc,
    string? Error);

public sealed record HubSpotRecord(
    string Id,
    string ObjectType,
    IReadOnlyDictionary<string, string> Properties,
    DateTimeOffset RetrievedAtUtc);

public sealed record HubSpotConnectorStatus(
    bool IsConnected,
    string? PortalId,
    DateTimeOffset? LastAuthenticatedAtUtc,
    DateTimeOffset? TokenExpiresAtUtc,
    long TotalRequests,
    long FailedRequests,
    int DailyRateLimitRemaining,
    DateTimeOffset StatusAsOfUtc);

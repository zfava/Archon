namespace ArchonAI.Core.Interfaces;

public interface IMicrosoft365Connector : IConnector
{
    global::System.Threading.Tasks.Task<M365AuthResult> AuthenticateAsync(CancellationToken cancellationToken = default);

    // Outlook
    global::System.Threading.Tasks.Task<IReadOnlyList<OutlookMessage>> GetEmailsAsync(
        string? filter,
        int top = 20,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<OutlookSendResult> SendEmailAsync(
        string to,
        string subject,
        string body,
        bool isHtml = false,
        CancellationToken cancellationToken = default);

    // Teams
    global::System.Threading.Tasks.Task<IReadOnlyList<TeamsChannelInfo>> GetTeamsChannelsAsync(
        string teamId,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<TeamsMessageResult> SendTeamsMessageAsync(
        string teamId,
        string channelId,
        string content,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<TeamsMessage>> GetTeamsMessagesAsync(
        string teamId,
        string channelId,
        int top = 20,
        CancellationToken cancellationToken = default);

    // SharePoint
    global::System.Threading.Tasks.Task<IReadOnlyList<SharePointItem>> GetSharePointItemsAsync(
        string siteId,
        string? listId,
        int top = 50,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<SharePointItem> GetSharePointItemAsync(
        string siteId,
        string driveId,
        string itemId,
        CancellationToken cancellationToken = default);

    // OneDrive
    global::System.Threading.Tasks.Task<IReadOnlyList<OneDriveItem>> ListOneDriveFilesAsync(
        string? folderId,
        int top = 50,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<OneDriveItem> GetOneDriveItemAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    M365ConnectorStatus GetStatus();
}

public sealed record M365AuthResult(
    bool IsAuthenticated,
    string? TenantId,
    string? UserPrincipalName,
    DateTimeOffset? ExpiresAtUtc,
    string? Error);

public sealed record OutlookMessage(
    string Id,
    string? From,
    string? Subject,
    string BodyPreview,
    bool IsRead,
    DateTimeOffset ReceivedAtUtc);

public sealed record OutlookSendResult(
    bool IsSuccess,
    string? MessageId,
    string? Error);

public sealed record TeamsChannelInfo(
    string Id,
    string DisplayName,
    string? Description,
    string MembershipType);

public sealed record TeamsMessageResult(
    bool IsSuccess,
    string? MessageId,
    string? Error);

public sealed record TeamsMessage(
    string Id,
    string? FromDisplayName,
    string BodyContent,
    DateTimeOffset CreatedAtUtc);

public sealed record SharePointItem(
    string Id,
    string Name,
    string? WebUrl,
    string? ContentType,
    long? SizeBytes,
    DateTimeOffset? ModifiedAtUtc,
    DateTimeOffset RetrievedAtUtc);

public sealed record OneDriveItem(
    string Id,
    string Name,
    string? MimeType,
    long? SizeBytes,
    string? ParentId,
    string? WebUrl,
    DateTimeOffset? ModifiedAtUtc,
    DateTimeOffset RetrievedAtUtc);

public sealed record M365ConnectorStatus(
    bool IsConnected,
    string? TenantId,
    string? UserPrincipalName,
    DateTimeOffset? LastAuthenticatedAtUtc,
    DateTimeOffset? TokenExpiresAtUtc,
    long TotalRequests,
    long FailedRequests,
    long EmailsSent,
    long TeamsMessagesSent,
    long SharePointOps,
    long OneDriveOps,
    int RateLimitRemaining,
    DateTimeOffset StatusAsOfUtc);

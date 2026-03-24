namespace ArchonAI.Core.Interfaces;

public interface ISlackConnector : IConnector
{
    global::System.Threading.Tasks.Task<SlackAuthResult> AuthenticateAsync(CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<SlackMessageResult> SendMessageAsync(
        string channel,
        string text,
        string? threadTs = null,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<SlackChannelInfo>> GetChannelsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<SlackMessage>> ReadChannelHistoryAsync(
        string channel,
        int limit = 50,
        string? oldest = null,
        string? latest = null,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<SlackMessageResult> PostAlertAsync(
        string channel,
        string alertLevel,
        string title,
        string details,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<bool> ProcessWebhookEventAsync(
        string requestBody,
        string signature,
        string timestamp,
        CancellationToken cancellationToken = default);

    SlackConnectorStatus GetStatus();
}

public sealed record SlackAuthResult(
    bool IsAuthenticated,
    string? TeamId,
    string? TeamName,
    string? BotUserId,
    DateTimeOffset? AuthenticatedAtUtc,
    string? Error);

public sealed record SlackMessageResult(
    bool IsSuccess,
    string? Channel,
    string? Timestamp,
    string? Error);

public sealed record SlackChannelInfo(
    string Id,
    string Name,
    bool IsPrivate,
    int MemberCount,
    string? Topic,
    string? Purpose);

public sealed record SlackMessage(
    string Ts,
    string? User,
    string Text,
    string? ThreadTs,
    DateTimeOffset SentAtUtc);

public sealed record SlackConnectorStatus(
    bool IsConnected,
    string? TeamId,
    string? TeamName,
    DateTimeOffset? LastAuthenticatedAtUtc,
    long TotalMessagesSent,
    long TotalMessagesRead,
    long FailedRequests,
    long WebhookEventsProcessed,
    int RateLimitRemaining,
    DateTimeOffset StatusAsOfUtc);

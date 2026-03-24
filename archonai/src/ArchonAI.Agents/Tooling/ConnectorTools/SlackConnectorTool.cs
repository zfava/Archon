using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Interfaces.Tooling;
using ArchonAI.Core.Models.Tooling;

namespace ArchonAI.Agents.Tooling.ConnectorTools;

public sealed class SlackConnectorTool : IAgentTool
{
    private readonly ISlackConnector _connector;

    public SlackConnectorTool(ISlackConnector connector)
    {
        _connector = connector;
    }

    public string Name => "connector.slack";

    public async global::System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken = default)
    {
        string action = request.Parameters.GetValueOrDefault("action", "send-message");

        return action switch
        {
            "send-message" => await SendMessageAsync(request, cancellationToken),
            "read-channel" => await ReadChannelAsync(request, cancellationToken),
            "get-channels" => await GetChannelsAsync(request, cancellationToken),
            "post-alert" => await PostAlertAsync(request, cancellationToken),
            "status" => GetStatus(),
            _ => new ToolExecutionResult(Name, false, new Dictionary<string, string>
            {
                ["error"] = $"Unknown Slack action: {action}"
            }, [$"Unsupported action: {action}"], DateTimeOffset.UtcNow)
        };
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> SendMessageAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string channel = request.Parameters.GetValueOrDefault("channel", string.Empty);
        string text = request.Parameters.GetValueOrDefault("text", string.Empty);
        string? threadTs = request.Parameters.GetValueOrDefault("threadTs");

        var result = await _connector.SendMessageAsync(channel, text, threadTs, ct);

        return new ToolExecutionResult(Name, result.IsSuccess, new Dictionary<string, string>
        {
            ["channel"] = result.Channel ?? string.Empty,
            ["timestamp"] = result.Timestamp ?? string.Empty,
            ["operation"] = "send-message"
        }, result.Error is not null ? [result.Error] : Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> ReadChannelAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string channel = request.Parameters.GetValueOrDefault("channel", string.Empty);
        _ = int.TryParse(request.Parameters.GetValueOrDefault("limit", "50"), out int limit);
        string? oldest = request.Parameters.GetValueOrDefault("oldest");
        string? latest = request.Parameters.GetValueOrDefault("latest");

        var messages = await _connector.ReadChannelHistoryAsync(channel, limit, oldest, latest, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["channel"] = channel,
            ["count"] = messages.Count.ToString(),
            ["messages"] = System.Text.Json.JsonSerializer.Serialize(messages)
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> GetChannelsAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        _ = int.TryParse(request.Parameters.GetValueOrDefault("limit", "100"), out int limit);

        var channels = await _connector.GetChannelsAsync(limit, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["count"] = channels.Count.ToString(),
            ["channels"] = System.Text.Json.JsonSerializer.Serialize(channels)
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> PostAlertAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string channel = request.Parameters.GetValueOrDefault("channel", string.Empty);
        string alertLevel = request.Parameters.GetValueOrDefault("alertLevel", "info");
        string title = request.Parameters.GetValueOrDefault("title", string.Empty);
        string details = request.Parameters.GetValueOrDefault("details", string.Empty);

        var result = await _connector.PostAlertAsync(channel, alertLevel, title, details, ct);

        return new ToolExecutionResult(Name, result.IsSuccess, new Dictionary<string, string>
        {
            ["channel"] = result.Channel ?? string.Empty,
            ["timestamp"] = result.Timestamp ?? string.Empty,
            ["alertLevel"] = alertLevel,
            ["operation"] = "post-alert"
        }, result.Error is not null ? [result.Error] : Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private ToolExecutionResult GetStatus()
    {
        var status = _connector.GetStatus();
        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["isConnected"] = status.IsConnected.ToString(),
            ["teamId"] = status.TeamId ?? "N/A",
            ["teamName"] = status.TeamName ?? "N/A",
            ["totalMessagesSent"] = status.TotalMessagesSent.ToString(),
            ["totalMessagesRead"] = status.TotalMessagesRead.ToString(),
            ["failedRequests"] = status.FailedRequests.ToString(),
            ["webhookEventsProcessed"] = status.WebhookEventsProcessed.ToString(),
            ["rateLimitRemaining"] = status.RateLimitRemaining.ToString()
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }
}

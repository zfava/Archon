using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Interfaces.Tooling;
using ArchonAI.Core.Models.Tooling;

namespace ArchonAI.Agents.Tooling.ConnectorTools;

public sealed class Microsoft365ConnectorTool : IAgentTool
{
    private readonly IMicrosoft365Connector _connector;

    public Microsoft365ConnectorTool(IMicrosoft365Connector connector)
    {
        _connector = connector;
    }

    public string Name => "connector.microsoft365";

    public async global::System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken = default)
    {
        string action = request.Parameters.GetValueOrDefault("action", "status");

        return action switch
        {
            "get-emails" => await GetEmailsAsync(request, cancellationToken),
            "send-email" => await SendEmailAsync(request, cancellationToken),
            "get-channels" => await GetTeamsChannelsAsync(request, cancellationToken),
            "send-teams-message" => await SendTeamsMessageAsync(request, cancellationToken),
            "get-teams-messages" => await GetTeamsMessagesAsync(request, cancellationToken),
            "get-sharepoint-items" => await GetSharePointItemsAsync(request, cancellationToken),
            "get-sharepoint-item" => await GetSharePointItemAsync(request, cancellationToken),
            "list-onedrive-files" => await ListOneDriveFilesAsync(request, cancellationToken),
            "get-onedrive-item" => await GetOneDriveItemAsync(request, cancellationToken),
            "status" => GetStatus(),
            _ => new ToolExecutionResult(Name, false, new Dictionary<string, string>
            {
                ["error"] = $"Unknown Microsoft 365 action: {action}"
            }, [$"Unsupported action: {action}"], DateTimeOffset.UtcNow)
        };
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> GetEmailsAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string? filter = request.Parameters.GetValueOrDefault("filter", null);
        _ = int.TryParse(request.Parameters.GetValueOrDefault("top", "20"), out int top);

        var messages = await _connector.GetEmailsAsync(filter, top, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["service"] = "outlook",
            ["count"] = messages.Count.ToString(),
            ["messages"] = System.Text.Json.JsonSerializer.Serialize(messages)
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> SendEmailAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string to = request.Parameters.GetValueOrDefault("to", string.Empty);
        string subject = request.Parameters.GetValueOrDefault("subject", string.Empty);
        string body = request.Parameters.GetValueOrDefault("body", string.Empty);
        bool isHtml = request.Parameters.GetValueOrDefault("isHtml", "false").Equals("true", StringComparison.OrdinalIgnoreCase);

        var result = await _connector.SendEmailAsync(to, subject, body, isHtml, ct);

        return new ToolExecutionResult(Name, result.IsSuccess, new Dictionary<string, string>
        {
            ["service"] = "outlook",
            ["operation"] = "send-email",
            ["messageId"] = result.MessageId ?? string.Empty
        }, result.Error is not null ? [result.Error] : Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> GetTeamsChannelsAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string teamId = request.Parameters.GetValueOrDefault("teamId", string.Empty);

        var channels = await _connector.GetTeamsChannelsAsync(teamId, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["service"] = "teams",
            ["count"] = channels.Count.ToString(),
            ["channels"] = System.Text.Json.JsonSerializer.Serialize(channels)
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> SendTeamsMessageAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string teamId = request.Parameters.GetValueOrDefault("teamId", string.Empty);
        string channelId = request.Parameters.GetValueOrDefault("channelId", string.Empty);
        string content = request.Parameters.GetValueOrDefault("content", string.Empty);

        var result = await _connector.SendTeamsMessageAsync(teamId, channelId, content, ct);

        return new ToolExecutionResult(Name, result.IsSuccess, new Dictionary<string, string>
        {
            ["service"] = "teams",
            ["operation"] = "send-teams-message",
            ["messageId"] = result.MessageId ?? string.Empty
        }, result.Error is not null ? [result.Error] : Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> GetTeamsMessagesAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string teamId = request.Parameters.GetValueOrDefault("teamId", string.Empty);
        string channelId = request.Parameters.GetValueOrDefault("channelId", string.Empty);
        _ = int.TryParse(request.Parameters.GetValueOrDefault("top", "20"), out int top);

        var messages = await _connector.GetTeamsMessagesAsync(teamId, channelId, top, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["service"] = "teams",
            ["count"] = messages.Count.ToString(),
            ["messages"] = System.Text.Json.JsonSerializer.Serialize(messages)
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> GetSharePointItemsAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string siteId = request.Parameters.GetValueOrDefault("siteId", string.Empty);
        string? listId = request.Parameters.GetValueOrDefault("listId", null);
        _ = int.TryParse(request.Parameters.GetValueOrDefault("top", "50"), out int top);

        var items = await _connector.GetSharePointItemsAsync(siteId, listId, top, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["service"] = "sharepoint",
            ["count"] = items.Count.ToString(),
            ["items"] = System.Text.Json.JsonSerializer.Serialize(items)
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> GetSharePointItemAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string siteId = request.Parameters.GetValueOrDefault("siteId", string.Empty);
        string driveId = request.Parameters.GetValueOrDefault("driveId", string.Empty);
        string itemId = request.Parameters.GetValueOrDefault("itemId", string.Empty);

        var item = await _connector.GetSharePointItemAsync(siteId, driveId, itemId, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["service"] = "sharepoint",
            ["itemId"] = item.Id,
            ["name"] = item.Name,
            ["webUrl"] = item.WebUrl ?? string.Empty,
            ["sizeBytes"] = item.SizeBytes?.ToString() ?? "N/A"
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> ListOneDriveFilesAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string? folderId = request.Parameters.GetValueOrDefault("folderId", null);
        _ = int.TryParse(request.Parameters.GetValueOrDefault("top", "50"), out int top);

        var files = await _connector.ListOneDriveFilesAsync(folderId, top, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["service"] = "onedrive",
            ["count"] = files.Count.ToString(),
            ["files"] = System.Text.Json.JsonSerializer.Serialize(files)
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> GetOneDriveItemAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string itemId = request.Parameters.GetValueOrDefault("itemId", string.Empty);

        var item = await _connector.GetOneDriveItemAsync(itemId, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["service"] = "onedrive",
            ["itemId"] = item.Id,
            ["name"] = item.Name,
            ["mimeType"] = item.MimeType ?? "N/A",
            ["sizeBytes"] = item.SizeBytes?.ToString() ?? "N/A",
            ["webUrl"] = item.WebUrl ?? string.Empty
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private ToolExecutionResult GetStatus()
    {
        var status = _connector.GetStatus();
        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["isConnected"] = status.IsConnected.ToString(),
            ["tenantId"] = status.TenantId ?? "N/A",
            ["userPrincipalName"] = status.UserPrincipalName ?? "N/A",
            ["totalRequests"] = status.TotalRequests.ToString(),
            ["failedRequests"] = status.FailedRequests.ToString(),
            ["emailsSent"] = status.EmailsSent.ToString(),
            ["teamsMessagesSent"] = status.TeamsMessagesSent.ToString(),
            ["sharePointOps"] = status.SharePointOps.ToString(),
            ["oneDriveOps"] = status.OneDriveOps.ToString(),
            ["rateLimitRemaining"] = status.RateLimitRemaining.ToString()
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }
}

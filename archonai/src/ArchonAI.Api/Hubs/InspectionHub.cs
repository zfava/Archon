using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ArchonAI.Api.Hubs;

/// <summary>
/// SignalR hub providing real-time inspection updates via WebSocket.
/// Clients subscribe to specific subjects (e.g., decisions, workflows) and receive
/// live policy evaluation, memory reference, and workflow diagnostics events.
/// </summary>
[Authorize]
public sealed class InspectionHub : Hub
{
    private readonly ILogger<InspectionHub> _logger;

    public InspectionHub(ILogger<InspectionHub> logger)
    {
        _logger = logger;
    }

    public override async global::System.Threading.Tasks.Task OnConnectedAsync()
    {
        _logger.LogDebug("Inspection hub client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async global::System.Threading.Tasks.Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug("Inspection hub client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Subscribe to inspection events for a specific subject.
    /// Group key: "inspection:{subjectType}:{subjectId}"
    /// </summary>
    public async global::System.Threading.Tasks.Task SubscribeToSubject(string subjectType, string subjectId)
    {
        var group = $"inspection:{subjectType}:{subjectId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        _logger.LogDebug("Client {ConnectionId} subscribed to {Group}", Context.ConnectionId, group);
    }

    /// <summary>
    /// Unsubscribe from inspection events for a specific subject.
    /// </summary>
    public async global::System.Threading.Tasks.Task UnsubscribeFromSubject(string subjectType, string subjectId)
    {
        var group = $"inspection:{subjectType}:{subjectId}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        _logger.LogDebug("Client {ConnectionId} unsubscribed from {Group}", Context.ConnectionId, group);
    }
}

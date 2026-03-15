using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Connectors.Implementations;

public sealed class MessagingConnector : IMessagingConnector
{
    private readonly ILogger<MessagingConnector> _logger;

    public MessagingConnector(ILogger<MessagingConnector> logger)
    {
        _logger = logger;
    }

    public string SystemName => "messaging";

    public global::System.Threading.Tasks.Task<string> SendMessageAsync(string channel, string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Messaging connector send to {Channel}", channel);
        return global::System.Threading.Tasks.Task.FromResult($"message-sent:{channel}");
    }

    public global::System.Threading.Tasks.Task PushResultAsync(ExecutionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Messaging connector received result for task {TaskId}", result.TaskId);
        return global::System.Threading.Tasks.Task.CompletedTask;
    }
}

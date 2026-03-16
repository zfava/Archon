using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Infrastructure.Connectors;

/// <summary>
/// Basic connector implementation for external tool dispatch.
/// </summary>
public sealed class DefaultConnector : IConnector
{
    private readonly ILogger<DefaultConnector> _logger;

    public DefaultConnector(ILogger<DefaultConnector> logger)
    {
        _logger = logger;
    }

    public string SystemName => "default";

    public global::System.Threading.Tasks.Task PushResultAsync(ExecutionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Connector {SystemName} received result {TaskId}: {Summary}", SystemName, result.TaskId, result.Summary);
        return global::System.Threading.Tasks.Task.CompletedTask;
    }
}

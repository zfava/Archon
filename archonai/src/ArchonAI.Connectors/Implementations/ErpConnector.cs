using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Connectors.Implementations;

public sealed class ErpConnector : IErpConnector
{
    private readonly ILogger<ErpConnector> _logger;

    public ErpConnector(ILogger<ErpConnector> logger)
    {
        _logger = logger;
    }

    public string SystemName => "erp";

    public global::System.Threading.Tasks.Task<string> SyncOrderAsync(string orderId, string payload, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("ERP sync for order {OrderId}", orderId);
        return global::System.Threading.Tasks.Task.FromResult($"erp-sync:{orderId}");
    }

    public global::System.Threading.Tasks.Task PushResultAsync(ExecutionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("ERP connector received result for task {TaskId}", result.TaskId);
        return global::System.Threading.Tasks.Task.CompletedTask;
    }
}

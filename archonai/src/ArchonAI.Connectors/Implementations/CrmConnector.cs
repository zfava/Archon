using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Connectors.Implementations;

public sealed class CrmConnector : ICrmConnector
{
    private readonly ILogger<CrmConnector> _logger;

    public CrmConnector(ILogger<CrmConnector> logger)
    {
        _logger = logger;
    }

    public string SystemName => "crm";

    public global::System.Threading.Tasks.Task<string> UpsertCustomerAsync(string customerId, string payload, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("CRM upsert for customer {CustomerId}", customerId);
        return global::System.Threading.Tasks.Task.FromResult($"crm-upsert:{customerId}");
    }

    public global::System.Threading.Tasks.Task PushResultAsync(ExecutionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("CRM connector received result for task {TaskId}", result.TaskId);
        return global::System.Threading.Tasks.Task.CompletedTask;
    }
}

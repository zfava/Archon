using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Connectors.Implementations;

public sealed class FinancialConnector : IFinancialConnector
{
    private readonly ILogger<FinancialConnector> _logger;

    public FinancialConnector(ILogger<FinancialConnector> logger)
    {
        _logger = logger;
    }

    public string SystemName => "financial";

    public global::System.Threading.Tasks.Task<string> PostTransactionAsync(string accountId, decimal amount, string currency, string description, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Financial post transaction to account {AccountId} amount {Amount} {Currency}", accountId, amount, currency);
        return global::System.Threading.Tasks.Task.FromResult($"financial-post:{accountId}:{amount}");
    }

    public global::System.Threading.Tasks.Task PushResultAsync(ExecutionResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Financial connector received result for task {TaskId}", result.TaskId);
        return global::System.Threading.Tasks.Task.CompletedTask;
    }
}

namespace ArchonAI.Core.Interfaces;

public interface IFinancialConnector : IConnector
{
    global::System.Threading.Tasks.Task<string> PostTransactionAsync(
        string accountId,
        decimal amount,
        string currency,
        string description,
        CancellationToken cancellationToken = default);
}

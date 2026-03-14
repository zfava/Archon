namespace ArchonAI.Core.Interfaces;

public interface ICrmConnector : IConnector
{
    global::System.Threading.Tasks.Task<string> UpsertCustomerAsync(
        string customerId,
        string payload,
        CancellationToken cancellationToken = default);
}

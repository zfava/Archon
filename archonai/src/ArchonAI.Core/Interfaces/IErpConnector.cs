namespace ArchonAI.Core.Interfaces;

public interface IErpConnector : IConnector
{
    global::System.Threading.Tasks.Task<string> SyncOrderAsync(
        string orderId,
        string payload,
        CancellationToken cancellationToken = default);
}

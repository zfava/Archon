namespace ArchonAI.Core.Interfaces;

public interface IMessagingConnector : IConnector
{
    global::System.Threading.Tasks.Task<string> SendMessageAsync(
        string channel,
        string message,
        CancellationToken cancellationToken = default);
}

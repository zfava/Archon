using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Interfaces.Tooling;
using ArchonAI.Core.Models.Tooling;

namespace ArchonAI.Agents.Tooling.ConnectorTools;

public sealed class MessagingConnectorTool : IAgentTool
{
    private readonly IMessagingConnector _connector;

    public MessagingConnectorTool(IMessagingConnector connector)
    {
        _connector = connector;
    }

    public string Name => "connector.messaging.send";

    public async global::System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken = default)
    {
        string channel = request.Parameters.GetValueOrDefault("channel", "general");
        string message = request.Parameters.GetValueOrDefault("message", string.Empty);

        string opId = await _connector.SendMessageAsync(channel, message, cancellationToken);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["operationId"] = opId,
            ["system"] = _connector.SystemName
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }
}

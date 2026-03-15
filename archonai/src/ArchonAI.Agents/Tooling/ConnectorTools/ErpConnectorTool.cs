using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Interfaces.Tooling;
using ArchonAI.Core.Models.Tooling;

namespace ArchonAI.Agents.Tooling.ConnectorTools;

public sealed class ErpConnectorTool : IAgentTool
{
    private readonly IErpConnector _connector;

    public ErpConnectorTool(IErpConnector connector)
    {
        _connector = connector;
    }

    public string Name => "connector.erp.sync-order";

    public async global::System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken = default)
    {
        string orderId = request.Parameters.GetValueOrDefault("orderId", "unknown");
        string payload = request.Parameters.GetValueOrDefault("payload", "{}");

        string opId = await _connector.SyncOrderAsync(orderId, payload, cancellationToken);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["operationId"] = opId,
            ["system"] = _connector.SystemName
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }
}

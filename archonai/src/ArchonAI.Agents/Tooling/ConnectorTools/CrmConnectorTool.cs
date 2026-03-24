using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Interfaces.Tooling;
using ArchonAI.Core.Models.Tooling;

namespace ArchonAI.Agents.Tooling.ConnectorTools;

public sealed class CrmConnectorTool : IAgentTool
{
    private readonly ICrmConnector _connector;

    public CrmConnectorTool(ICrmConnector connector)
    {
        _connector = connector;
    }

    public string Name => "connector.crm.upsert-customer";

    public async global::System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken = default)
    {
        string customerId = request.Parameters.GetValueOrDefault("customerId", "unknown");
        string payload = request.Parameters.GetValueOrDefault("payload", "{}");

        string opId = await _connector.UpsertCustomerAsync(customerId, payload, cancellationToken);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["operationId"] = opId,
            ["system"] = _connector.SystemName
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }
}

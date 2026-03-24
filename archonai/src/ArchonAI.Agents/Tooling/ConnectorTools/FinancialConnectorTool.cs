using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Interfaces.Tooling;
using ArchonAI.Core.Models.Tooling;

namespace ArchonAI.Agents.Tooling.ConnectorTools;

public sealed class FinancialConnectorTool : IAgentTool
{
    private readonly IFinancialConnector _connector;

    public FinancialConnectorTool(IFinancialConnector connector)
    {
        _connector = connector;
    }

    public string Name => "connector.financial.post-transaction";

    public async global::System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken = default)
    {
        string accountId = request.Parameters.GetValueOrDefault("accountId", "unknown");
        string currency = request.Parameters.GetValueOrDefault("currency", "USD");
        string description = request.Parameters.GetValueOrDefault("description", "tool-transaction");

        _ = decimal.TryParse(request.Parameters.GetValueOrDefault("amount", "0"), out decimal amount);

        string opId = await _connector.PostTransactionAsync(accountId, amount, currency, description, cancellationToken);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["operationId"] = opId,
            ["system"] = _connector.SystemName
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }
}

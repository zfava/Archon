using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Interfaces.Tooling;
using ArchonAI.Core.Models.Tooling;

namespace ArchonAI.Agents.Tooling.ConnectorTools;

public sealed class SalesforceConnectorTool : IAgentTool
{
    private readonly ISalesforceConnector _connector;

    public SalesforceConnectorTool(ISalesforceConnector connector)
    {
        _connector = connector;
    }

    public string Name => "connector.salesforce";

    public async global::System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken = default)
    {
        string action = request.Parameters.GetValueOrDefault("action", "query-accounts");

        return action switch
        {
            "query-accounts" => await QueryAccountsAsync(request, cancellationToken),
            "query-contacts" => await QueryContactsAsync(request, cancellationToken),
            "query-opportunities" => await QueryOpportunitiesAsync(request, cancellationToken),
            "create-record" => await CreateRecordAsync(request, cancellationToken),
            "update-record" => await UpdateRecordAsync(request, cancellationToken),
            "status" => GetStatus(),
            _ => new ToolExecutionResult(Name, false, new Dictionary<string, string>
            {
                ["error"] = $"Unknown Salesforce action: {action}"
            }, [$"Unsupported action: {action}"], DateTimeOffset.UtcNow)
        };
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> QueryAccountsAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string filter = request.Parameters.GetValueOrDefault("filter", string.Empty);
        var records = await _connector.QueryAccountsAsync(filter, ct);
        return BuildQueryResult("Account", records);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> QueryContactsAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string filter = request.Parameters.GetValueOrDefault("filter", string.Empty);
        var records = await _connector.QueryContactsAsync(filter, ct);
        return BuildQueryResult("Contact", records);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> QueryOpportunitiesAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string filter = request.Parameters.GetValueOrDefault("filter", string.Empty);
        var records = await _connector.QueryOpportunitiesAsync(filter, ct);
        return BuildQueryResult("Opportunity", records);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> CreateRecordAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string objectType = request.Parameters.GetValueOrDefault("objectType", string.Empty);
        var fields = request.Parameters
            .Where(kv => kv.Key.StartsWith("field.", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                kv => kv.Key["field.".Length..],
                kv => kv.Value,
                StringComparer.OrdinalIgnoreCase) as IReadOnlyDictionary<string, string>;

        string recordId = await _connector.CreateRecordAsync(objectType, fields, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["recordId"] = recordId,
            ["objectType"] = objectType,
            ["operation"] = "create"
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> UpdateRecordAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string objectType = request.Parameters.GetValueOrDefault("objectType", string.Empty);
        string recordId = request.Parameters.GetValueOrDefault("recordId", string.Empty);
        var fields = request.Parameters
            .Where(kv => kv.Key.StartsWith("field.", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                kv => kv.Key["field.".Length..],
                kv => kv.Value,
                StringComparer.OrdinalIgnoreCase) as IReadOnlyDictionary<string, string>;

        await _connector.UpdateRecordAsync(objectType, recordId, fields, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["recordId"] = recordId,
            ["objectType"] = objectType,
            ["operation"] = "update"
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private ToolExecutionResult GetStatus()
    {
        var status = _connector.GetStatus();
        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["isConnected"] = status.IsConnected.ToString(),
            ["instanceUrl"] = status.InstanceUrl ?? "N/A",
            ["totalRequests"] = status.TotalRequests.ToString(),
            ["failedRequests"] = status.FailedRequests.ToString(),
            ["rateLimitRemaining"] = status.RateLimitRemaining.ToString()
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private ToolExecutionResult BuildQueryResult(string objectType, IReadOnlyList<SalesforceRecord> records)
    {
        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["objectType"] = objectType,
            ["count"] = records.Count.ToString(),
            ["records"] = System.Text.Json.JsonSerializer.Serialize(records)
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }
}

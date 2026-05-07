using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Interfaces.Tooling;
using ArchonAI.Core.Models.Tooling;

namespace ArchonAI.Agents.Tooling.ConnectorTools;

public sealed class HubSpotConnectorTool : IAgentTool
{
    private readonly IHubSpotConnector _connector;

    public HubSpotConnectorTool(IHubSpotConnector connector)
    {
        _connector = connector;
    }

    public string Name => "connector.hubspot";

    public async global::System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken = default)
    {
        string action = request.Parameters.GetValueOrDefault("action", "get-contacts");

        return action switch
        {
            "get-contacts" => await GetContactsAsync(request, cancellationToken),
            "get-deals" => await GetDealsAsync(request, cancellationToken),
            "update-pipeline" => await UpdatePipelineAsync(request, cancellationToken),
            "create-record" => await CreateRecordAsync(request, cancellationToken),
            "status" => GetStatus(),
            _ => new ToolExecutionResult(Name, false, new Dictionary<string, string>
            {
                ["error"] = $"Unknown HubSpot action: {action}"
            }, [$"Unsupported action: {action}"], DateTimeOffset.UtcNow)
        };
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> GetContactsAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string filter = request.Parameters.GetValueOrDefault("filter", string.Empty);
        _ = int.TryParse(request.Parameters.GetValueOrDefault("limit", "100"), out int limit);
        var records = await _connector.GetContactsAsync(filter, limit, ct);
        return BuildQueryResult("Contact", records);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> GetDealsAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string filter = request.Parameters.GetValueOrDefault("filter", string.Empty);
        _ = int.TryParse(request.Parameters.GetValueOrDefault("limit", "100"), out int limit);
        var records = await _connector.GetDealsAsync(filter, limit, ct);
        return BuildQueryResult("Deal", records);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> UpdatePipelineAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string objectType = request.Parameters.GetValueOrDefault("objectType", "deals");
        string recordId = request.Parameters.GetValueOrDefault("recordId", string.Empty);
        var properties = ExtractFieldProperties(request);

        await _connector.UpdatePipelineRecordAsync(objectType, recordId, properties, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["recordId"] = recordId,
            ["objectType"] = objectType,
            ["operation"] = "update-pipeline"
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private async global::System.Threading.Tasks.Task<ToolExecutionResult> CreateRecordAsync(ToolExecutionRequest request, CancellationToken ct)
    {
        string objectType = request.Parameters.GetValueOrDefault("objectType", string.Empty);
        var properties = ExtractFieldProperties(request);

        string recordId = await _connector.CreateRecordAsync(objectType, properties, ct);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["recordId"] = recordId,
            ["objectType"] = objectType,
            ["operation"] = "create"
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private ToolExecutionResult GetStatus()
    {
        var status = _connector.GetStatus();
        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["isConnected"] = status.IsConnected.ToString(),
            ["portalId"] = status.PortalId ?? "N/A",
            ["totalRequests"] = status.TotalRequests.ToString(),
            ["failedRequests"] = status.FailedRequests.ToString(),
            ["dailyRateLimitRemaining"] = status.DailyRateLimitRemaining.ToString()
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private static ToolExecutionResult BuildQueryResult(string objectType, IReadOnlyList<HubSpotRecord> records)
    {
        return new ToolExecutionResult("connector.hubspot", true, new Dictionary<string, string>
        {
            ["objectType"] = objectType,
            ["count"] = records.Count.ToString(),
            ["records"] = System.Text.Json.JsonSerializer.Serialize(records)
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }

    private static IReadOnlyDictionary<string, string> ExtractFieldProperties(ToolExecutionRequest request)
    {
        return request.Parameters
            .Where(kv => kv.Key.StartsWith("field.", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                kv => kv.Key["field.".Length..],
                kv => kv.Value,
                StringComparer.OrdinalIgnoreCase);
    }
}

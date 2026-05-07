using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Interfaces.Tooling;
using ArchonAI.Core.Models.Tooling;

namespace ArchonAI.Agents.Tooling;

public sealed class DataFabricQueryTool : IAgentTool
{
    private readonly IDataFabricEngine _dataFabricEngine;
    private readonly IMultiTenantContext _tenantContext;

    public DataFabricQueryTool(IDataFabricEngine dataFabricEngine, IMultiTenantContext tenantContext)
    {
        _dataFabricEngine = dataFabricEngine;
        _tenantContext = tenantContext;
    }

    public string Name => "datafabric.query";

    public async global::System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(
        ToolExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        string source = request.Parameters.GetValueOrDefault("source", "*");
        IReadOnlyDictionary<string, string> filters = ParsePairs(request.Parameters.GetValueOrDefault("filters", string.Empty));
        IReadOnlyDictionary<string, string> schemaMap = ParsePairs(request.Parameters.GetValueOrDefault("schemaMap", string.Empty));
        IReadOnlyList<string> permissions = request.Parameters.GetValueOrDefault("permissions", "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string tenantId = request.Parameters.GetValueOrDefault("tenantId", _tenantContext.CurrentTenantId);
        using IDisposable tenantScope = _tenantContext.BeginTenantScope(tenantId);

        var result = await _dataFabricEngine.QueryEnterpriseDataAsync(
            source,
            filters,
            schemaMap,
            permissions,
            consumerType: "agent",
            cancellationToken);

        return new ToolExecutionResult(
            Name,
            result.IsAllowed,
            new Dictionary<string, string>
            {
                ["rowCount"] = result.Rows.Count.ToString(),
                ["reason"] = result.Reason
            },
            result.IsAllowed ? Array.Empty<string>() : new[] { result.Reason },
            DateTimeOffset.UtcNow);
    }

    private static IReadOnlyDictionary<string, string> ParsePairs(string raw)
    {
        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(pair => pair.Length == 2 && !string.IsNullOrWhiteSpace(pair[0]))
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.OrdinalIgnoreCase);
    }
}

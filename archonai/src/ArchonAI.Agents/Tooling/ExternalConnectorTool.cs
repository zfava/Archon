using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Interfaces.Tooling;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Tooling;

namespace ArchonAI.Agents.Tooling;

/// <summary>
/// Tool for invoking external connectors.
/// </summary>
public sealed class ExternalConnectorTool : IAgentTool
{
    private readonly IReadOnlyDictionary<string, IConnector> _connectors;

    public ExternalConnectorTool(IEnumerable<IConnector> connectors)
    {
        _connectors = connectors.ToDictionary(c => c.SystemName, StringComparer.OrdinalIgnoreCase);
    }

    public string Name => "external.connector";

    public async global::System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken = default)
    {
        string connectorName = request.Parameters.GetValueOrDefault("system", string.Empty);
        if (!_connectors.TryGetValue(connectorName, out IConnector? connector))
        {
            return new ToolExecutionResult(Name, false, new Dictionary<string, string>(), new[] { $"Connector '{connectorName}' not found." }, DateTimeOffset.UtcNow);
        }

        var result = new ExecutionResult(
            TaskId: Guid.NewGuid(),
            IsSuccess: true,
            Summary: request.Parameters.GetValueOrDefault("payload", "Tool payload"),
            Outputs: new Dictionary<string, string>(),
            Warnings: Array.Empty<string>(),
            Errors: Array.Empty<string>(),
            CompletedAtUtc: DateTimeOffset.UtcNow);

        await connector.PushResultAsync(result, cancellationToken);

        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["connector"] = connector.SystemName,
            ["status"] = "dispatched"
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }
}

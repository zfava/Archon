using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Interfaces.Tooling;
using ArchonAI.Core.Models.Tooling;
using ArchonAI.Core.Models.Trace;

namespace ArchonAI.Agents.Tooling;

/// <summary>
/// Thread-safe agent tooling registry/executor.
/// </summary>
public sealed class AgentToolingFramework : IAgentToolRegistry, IAgentToolExecutor
{
    private readonly ConcurrentDictionary<string, IAgentTool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly ITraceStore _traceStore;

    public AgentToolingFramework(ITraceStore traceStore)
    {
        _traceStore = traceStore;
    }

    public void RegisterTool(IAgentTool tool)
    {
        _tools[tool.Name] = tool;
    }

    public IReadOnlyList<string> GetAvailableTools() => _tools.Keys.OrderBy(x => x).ToArray();

    public async global::System.Threading.Tasks.Task<ToolExecutionResult> ExecuteToolAsync(
        ToolExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        await _traceStore.RecordAsync(new TraceEntry(
            Id: Guid.NewGuid(),
            Scope: $"requester:{request.RequestedBy}",
            Category: "tool-call",
            Message: $"Executing tool '{request.ToolName}'.",
            Metadata: new Dictionary<string, string>
            {
                ["toolName"] = request.ToolName,
                ["requestedBy"] = request.RequestedBy
            },
            RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        if (!_tools.TryGetValue(request.ToolName, out IAgentTool? tool))
        {
            await _traceStore.RecordAsync(new TraceEntry(
                Id: Guid.NewGuid(),
                Scope: $"requester:{request.RequestedBy}",
                Category: "tool-call",
                Message: $"Tool '{request.ToolName}' not registered.",
                Metadata: new Dictionary<string, string>
                {
                    ["toolName"] = request.ToolName,
                    ["status"] = "not-found"
                },
                RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);

            return new ToolExecutionResult(
                ToolName: request.ToolName,
                IsSuccess: false,
                Outputs: new Dictionary<string, string>(),
                Errors: new[] { $"Tool '{request.ToolName}' is not registered." },
                CompletedAtUtc: DateTimeOffset.UtcNow);
        }
        ToolExecutionResult result = await tool.ExecuteAsync(request, cancellationToken);

        await _traceStore.RecordAsync(new TraceEntry(
            Id: Guid.NewGuid(),
            Scope: $"requester:{request.RequestedBy}",
            Category: "tool-call",
            Message: $"Tool '{request.ToolName}' execution completed.",
            Metadata: new Dictionary<string, string>
            {
                ["toolName"] = request.ToolName,
                ["isSuccess"] = result.IsSuccess.ToString(),
                ["errorCount"] = result.Errors.Count.ToString()
            },
            RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);

        return result;
    }
}

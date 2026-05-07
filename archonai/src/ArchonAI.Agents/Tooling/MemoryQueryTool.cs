using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Interfaces.Tooling;
using ArchonAI.Core.Models.Tooling;

namespace ArchonAI.Agents.Tooling;

/// <summary>
/// Tool for querying memory records, including semantic retrieval.
/// </summary>
public sealed class MemoryQueryTool : IAgentTool
{
    private readonly IMemoryStore _memoryStore;

    public MemoryQueryTool(IMemoryStore memoryStore)
    {
        _memoryStore = memoryStore;
    }

    public string Name => "memory.query";

    public async global::System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken = default)
    {
        string scope = request.Parameters.GetValueOrDefault("scope", "default");

        if (request.Parameters.TryGetValue("embedding", out string? embeddingText) && !string.IsNullOrWhiteSpace(embeddingText))
        {
            float[] embedding = embeddingText
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(float.Parse)
                .ToArray();

            var records = await _memoryStore.SemanticSearchAsync(scope, embedding, topK: 5, cancellationToken);
            return new ToolExecutionResult(Name, true, new Dictionary<string, string>
            {
                ["count"] = records.Count.ToString(),
                ["mode"] = "semantic"
            }, Array.Empty<string>(), DateTimeOffset.UtcNow);
        }

        var byScope = await _memoryStore.QueryByScopeAsync(scope, cancellationToken);
        return new ToolExecutionResult(Name, true, new Dictionary<string, string>
        {
            ["count"] = byScope.Count.ToString(),
            ["mode"] = "scope"
        }, Array.Empty<string>(), DateTimeOffset.UtcNow);
    }
}

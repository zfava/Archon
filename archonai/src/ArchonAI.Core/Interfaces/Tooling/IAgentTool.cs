using ArchonAI.Core.Models.Tooling;

namespace ArchonAI.Core.Interfaces.Tooling;

public interface IAgentTool
{
    string Name { get; }

    global::System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(
        ToolExecutionRequest request,
        CancellationToken cancellationToken = default);
}

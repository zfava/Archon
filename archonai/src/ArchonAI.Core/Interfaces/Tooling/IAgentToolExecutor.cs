using ArchonAI.Core.Models.Tooling;

namespace ArchonAI.Core.Interfaces.Tooling;

public interface IAgentToolExecutor
{
    global::System.Threading.Tasks.Task<ToolExecutionResult> ExecuteToolAsync(
        ToolExecutionRequest request,
        CancellationToken cancellationToken = default);
}

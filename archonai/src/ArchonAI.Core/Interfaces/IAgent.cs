using ArchonAI.Core.Models;
using CoreTask = ArchonAI.Core.Models.Task;
using CoreExecutionContext = ArchonAI.Core.Models.ExecutionContext;

namespace ArchonAI.Core.Interfaces;

public interface IAgent
{
    Agent Describe();

    global::System.Threading.Tasks.Task<ExecutionResult> ExecuteAsync(
        CoreTask task,
        CoreExecutionContext context,
        CancellationToken cancellationToken = default);
}

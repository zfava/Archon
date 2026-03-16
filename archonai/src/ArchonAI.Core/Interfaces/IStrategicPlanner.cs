using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Planning;

namespace ArchonAI.Core.Interfaces;

public interface IStrategicPlanner
{
    global::System.Threading.Tasks.Task<WorkflowDefinition> BuildWorkflowAsync(
        Objective objective,
        CancellationToken cancellationToken = default);
}

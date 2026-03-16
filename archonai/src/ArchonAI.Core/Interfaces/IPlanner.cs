using ArchonAI.Core.Models;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Core.Interfaces;

public interface IPlanner
{
    global::System.Threading.Tasks.Task<IReadOnlyList<CoreTask>> CreatePlanAsync(
        Objective objective,
        CancellationToken cancellationToken = default);
}

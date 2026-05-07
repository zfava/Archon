using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Scheduler;
using CoreTask = ArchonAI.Core.Models.Task;

namespace ArchonAI.Core.Interfaces;

public interface IResourceScheduler
{
    global::System.Threading.Tasks.Task<SchedulePlan> CreateExecutionPlanAsync(
        IReadOnlyList<CoreTask> tasks,
        int requestedMaxParallelism,
        CancellationToken cancellationToken = default);
}

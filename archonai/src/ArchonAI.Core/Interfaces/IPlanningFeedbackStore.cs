using ArchonAI.Core.Models;

namespace ArchonAI.Core.Interfaces;

public interface IPlanningFeedbackStore
{
    global::System.Threading.Tasks.Task AddAsync(PlanningFeedback feedback, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<PlanningFeedback>> GetRecentAsync(int maxCount, CancellationToken cancellationToken = default);
}

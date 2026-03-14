using System.Collections.Concurrent;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;

namespace ArchonAI.Infrastructure.Services;

public sealed class InMemoryPlanningFeedbackStore : IPlanningFeedbackStore
{
    private readonly ConcurrentQueue<PlanningFeedback> _feedbackItems = new();

    public global::System.Threading.Tasks.Task AddAsync(PlanningFeedback feedback, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _feedbackItems.Enqueue(feedback);
        return global::System.Threading.Tasks.Task.CompletedTask;
    }

    public global::System.Threading.Tasks.Task<IReadOnlyList<PlanningFeedback>> GetRecentAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<PlanningFeedback> items = _feedbackItems
            .Reverse()
            .Take(Math.Max(0, maxCount))
            .Reverse()
            .ToArray();

        return global::System.Threading.Tasks.Task.FromResult(items);
    }
}

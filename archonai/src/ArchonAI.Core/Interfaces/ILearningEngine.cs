using ArchonAI.Core.Models.Learning;
using ArchonAI.Core.Models.Patterns;

namespace ArchonAI.Core.Interfaces;

public interface ILearningEngine
{
    global::System.Threading.Tasks.Task IngestPatternsAsync(
        string tenantId,
        IReadOnlyList<OperationalPattern> patterns,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<LearningInsight>> GetGlobalInsightsAsync(
        CancellationToken cancellationToken = default);
}

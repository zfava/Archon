using ArchonAI.Core.Models.Patterns;

namespace ArchonAI.Core.Interfaces;

public interface IPatternDiscoveryEngine
{
    global::System.Threading.Tasks.Task<IReadOnlyList<OperationalPattern>> DiscoverObjectivePatternsAsync(
        Guid objectiveId,
        CancellationToken cancellationToken = default);
}

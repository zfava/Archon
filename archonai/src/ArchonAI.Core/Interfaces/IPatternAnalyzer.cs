using ArchonAI.Core.Models.Patterns;

namespace ArchonAI.Core.Interfaces;

public interface IPatternAnalyzer
{
    global::System.Threading.Tasks.Task<IReadOnlyList<OperationalPattern>> AnalyzeObjectiveAsync(
        Guid objectiveId,
        CancellationToken cancellationToken = default);
}

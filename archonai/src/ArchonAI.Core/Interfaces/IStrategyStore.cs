using ArchonAI.Core.Models.Planning;

namespace ArchonAI.Core.Interfaces;

public interface IStrategyStore
{
    global::System.Threading.Tasks.Task SaveAsync(OperationalStrategy strategy, CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<IReadOnlyList<OperationalStrategy>> QueryByObjectiveTypeAsync(
        string objectiveType,
        CancellationToken cancellationToken = default);
}

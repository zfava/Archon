using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Perception;

namespace ArchonAI.Core.Interfaces;

public interface IPerceptionEngine
{
    global::System.Threading.Tasks.Task<PerceptionResult> ProcessObjectiveAsync(
        Objective objective,
        CancellationToken cancellationToken = default);
}

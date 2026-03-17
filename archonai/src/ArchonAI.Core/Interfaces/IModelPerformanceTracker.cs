using ArchonAI.Core.Models.Models.Routing;

namespace ArchonAI.Core.Interfaces;

public interface IModelPerformanceTracker
{
    void RecordOutcome(string provider, string model, bool success, double latencyMs, double cost, double? accuracy);

    ModelPerformanceScore? GetScore(string provider, string model);

    IReadOnlyList<ModelPerformanceScore> GetAllScores();

    ModelPerformanceScore? GetBestModelForStrategy(string strategy);
}

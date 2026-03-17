using ArchonAI.Core.Models.Models.Routing;

namespace ArchonAI.Core.Interfaces;

public interface IModelPerformanceTracker
{
    void RecordOutcome(string provider, string model, bool success, double latencyMs, double cost, double? accuracy);

    void RecordOutcome(string provider, string model, string taskType, bool success, double latencyMs, double cost, double? accuracy);

    ModelPerformanceScore? GetScore(string provider, string model);

    IReadOnlyList<ModelPerformanceScore> GetAllScores();

    ModelPerformanceScore? GetBestModelForStrategy(string strategy);

    IReadOnlyList<ModelRoutingWeight> GetRoutingWeights();

    ModelRoutingWeight? GetRoutingWeight(string provider, string model);

    IReadOnlyList<TaskTypeModelWeight> GetTaskTypeWeights(string taskType);

    void SetRoutingWeight(string provider, string model, double weight, string reason);

    void SetTaskTypeWeight(string taskType, string provider, string model, double weight);

    ModelPerformanceScore? SelectByWeight(string strategy, string? taskType = null);
}

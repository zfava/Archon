namespace ArchonAI.Core.Models.Models.Routing;

public sealed record ModelPerformanceScore(
    string Provider,
    string Model,
    double AverageLatencyMs,
    double AverageCostPerRequest,
    double AccuracyRate,
    double SuccessRate,
    int SampleCount,
    double CompositeScore,
    DateTimeOffset LastUpdatedUtc);

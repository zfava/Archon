namespace ArchonAI.Core.Models.Simulation;

public sealed record SimulationResult(
    Guid SimulationId,
    string Strategy,
    double PredictedSuccessProbability,
    double PredictedFailureProbability,
    double PredictedLatencyMs,
    decimal PredictedCost,
    IReadOnlyList<string> PredictedRisks,
    DateTimeOffset SimulatedAtUtc);

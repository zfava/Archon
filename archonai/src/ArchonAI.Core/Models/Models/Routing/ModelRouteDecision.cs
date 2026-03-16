namespace ArchonAI.Core.Models.Models.Routing;

public sealed record ModelRouteDecision(
    string Provider,
    string Model,
    string Reason,
    bool CostOptimized,
    bool LatencyOptimized,
    DateTimeOffset RoutedAtUtc);

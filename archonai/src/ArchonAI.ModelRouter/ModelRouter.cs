using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Models;
using ArchonAI.Core.Models.Models.Routing;
using Microsoft.Extensions.Options;

namespace ArchonAI.ModelRouter;

public sealed class ModelRouter : IModelRouter
{
    private readonly ModelRouterOptions _options;

    public ModelRouter(IOptions<ModelRouterOptions> options)
    {
        _options = options.Value;
    }

    public ModelRouteDecision Route(ModelRequest request)
    {
        string explicitModel = request.Model?.Trim() ?? string.Empty;
        bool preferCost = request.Parameters.TryGetValue("optimize", out string? optimize)
            && optimize.Equals("cost", StringComparison.OrdinalIgnoreCase);
        bool preferLatency = request.Parameters.TryGetValue("optimize", out optimize)
            && optimize.Equals("latency", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(explicitModel))
        {
            string explicitProvider = ResolveProviderFromModel(explicitModel);
            return new ModelRouteDecision(
                Provider: explicitProvider,
                Model: explicitModel,
                Reason: "explicit-model-requested",
                CostOptimized: false,
                LatencyOptimized: false,
                RoutedAtUtc: DateTimeOffset.UtcNow);
        }

        if (request.Parameters.TryGetValue("taskType", out string? taskType)
            && !string.IsNullOrWhiteSpace(taskType)
            && _options.TaskTypeModelMap.TryGetValue(taskType, out string? mappedModel))
        {
            return new ModelRouteDecision(
                Provider: ResolveProviderFromModel(mappedModel),
                Model: mappedModel,
                Reason: $"task-type-route:{taskType}",
                CostOptimized: false,
                LatencyOptimized: false,
                RoutedAtUtc: DateTimeOffset.UtcNow);
        }

        if (preferCost)
        {
            return new ModelRouteDecision(
                Provider: _options.CostOptimizedProvider,
                Model: _options.CostOptimizedModel,
                Reason: "cost-optimization",
                CostOptimized: true,
                LatencyOptimized: false,
                RoutedAtUtc: DateTimeOffset.UtcNow);
        }

        if (preferLatency)
        {
            return new ModelRouteDecision(
                Provider: _options.LatencyOptimizedProvider,
                Model: _options.LatencyOptimizedModel,
                Reason: "latency-optimization",
                CostOptimized: false,
                LatencyOptimized: true,
                RoutedAtUtc: DateTimeOffset.UtcNow);
        }

        if (request.Parameters.TryGetValue("optimize", out string? quality)
            && quality.Equals("quality", StringComparison.OrdinalIgnoreCase))
        {
            return new ModelRouteDecision(
                Provider: _options.QualityOptimizedProvider,
                Model: _options.QualityOptimizedModel,
                Reason: "quality-optimization",
                CostOptimized: false,
                LatencyOptimized: false,
                RoutedAtUtc: DateTimeOffset.UtcNow);
        }

        return new ModelRouteDecision(
            Provider: _options.DefaultProvider,
            Model: _options.DefaultModel,
            Reason: "default-route",
            CostOptimized: false,
            LatencyOptimized: false,
            RoutedAtUtc: DateTimeOffset.UtcNow);
    }

    private static string ResolveProviderFromModel(string model)
    {
        if (model.StartsWith("openai", StringComparison.OrdinalIgnoreCase))
        {
            return "openai";
        }

        if (model.StartsWith("azure", StringComparison.OrdinalIgnoreCase))
        {
            return "azure-openai";
        }

        if (model.StartsWith("anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return "anthropic";
        }

        if (model.StartsWith("local", StringComparison.OrdinalIgnoreCase))
        {
            return "local";
        }

        return "local";
    }
}

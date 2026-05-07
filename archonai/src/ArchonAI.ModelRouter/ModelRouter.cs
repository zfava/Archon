using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Models;
using ArchonAI.Core.Models.Models.Routing;
using Microsoft.Extensions.Options;

namespace ArchonAI.ModelRouter;

public sealed class ModelRouter : IModelRouter
{
    private readonly ModelRouterOptions _options;
    private readonly IModelPerformanceTracker _performanceTracker;

    public ModelRouter(IOptions<ModelRouterOptions> options, IModelPerformanceTracker performanceTracker)
    {
        _options = options.Value;
        _performanceTracker = performanceTracker;
    }

    public ModelRouteDecision Route(ModelRequest request)
    {
        string explicitModel = request.Model?.Trim() ?? string.Empty;
        string strategy = ResolveStrategy(request);

        // 1. Explicit model request — highest priority
        if (!string.IsNullOrWhiteSpace(explicitModel))
        {
            string explicitProvider = ResolveProviderFromModel(explicitModel);
            return BuildDecision(explicitProvider, explicitModel, "explicit-model-requested", strategy);
        }

        // 2. Task-type based routing — weighted selection when weights exist
        request.Parameters.TryGetValue("taskType", out string? taskType);
        if (!string.IsNullOrWhiteSpace(taskType) && _options.EnableAdaptiveRouting)
        {
            var weightedResult = _performanceTracker.SelectByWeight(strategy, taskType);
            if (weightedResult is not null)
            {
                var routingWeight = _performanceTracker.GetRoutingWeight(weightedResult.Provider, weightedResult.Model);
                string weightInfo = routingWeight is not null ? $":w={routingWeight.Weight:F2}" : "";
                return BuildDecision(
                    weightedResult.Provider,
                    weightedResult.Model,
                    $"weighted-task-type:{taskType}{weightInfo}:score-{weightedResult.CompositeScore:F3}",
                    strategy);
            }
        }

        // 2b. Fallback to static task-type map
        if (!string.IsNullOrWhiteSpace(taskType)
            && _options.TaskTypeModelMap.TryGetValue(taskType, out string? mappedModel))
        {
            return BuildDecision(ResolveProviderFromModel(mappedModel), mappedModel, $"task-type-route:{taskType}", strategy);
        }

        // 3. Adaptive routing — weighted selection across all models
        if (_options.EnableAdaptiveRouting)
        {
            var weightedGlobal = _performanceTracker.SelectByWeight(strategy);
            if (weightedGlobal is not null)
            {
                var routingWeight = _performanceTracker.GetRoutingWeight(weightedGlobal.Provider, weightedGlobal.Model);
                string weightInfo = routingWeight is not null ? $":w={routingWeight.Weight:F2}" : "";
                return BuildDecision(
                    weightedGlobal.Provider,
                    weightedGlobal.Model,
                    $"weighted-adaptive:{strategy}{weightInfo}:score-{weightedGlobal.CompositeScore:F3}",
                    strategy);
            }

            // Fallback to deterministic best-model selection
            var adaptiveDecision = TryAdaptiveRoute(strategy);
            if (adaptiveDecision is not null)
            {
                return adaptiveDecision;
            }
        }

        // 4. Strategy-based static routing
        return RouteByStrategy(strategy);
    }

    public ModelFallbackChain? GetFallbackChain(string chainName)
    {
        if (!_options.FallbackChains.TryGetValue(chainName, out var entries))
            return null;

        return new ModelFallbackChain(
            Name: chainName,
            Entries: entries
                .OrderBy(e => e.Priority)
                .Select(e => new ModelFallbackEntry(e.Provider, e.Model, e.Priority))
                .ToList());
    }

    public ModelRouteDecision? RouteWithFallback(ModelRequest request, string? failedProvider = null, string? failedModel = null)
    {
        string strategy = ResolveStrategy(request);
        string chainName = strategy is "cost" or "latency" or "quality" ? strategy : "default";

        var chain = GetFallbackChain(chainName);
        if (chain is null) return null;

        foreach (var entry in chain.Entries)
        {
            if (entry.Provider == failedProvider && entry.Model == failedModel)
                continue;

            var score = _performanceTracker.GetScore(entry.Provider, entry.Model);
            if (score is not null && score.SuccessRate < 0.1 && score.SampleCount >= _options.MinSamplesForAdaptive)
                continue;

            return BuildDecision(entry.Provider, entry.Model, $"fallback-chain:{chainName}:priority-{entry.Priority}", strategy);
        }

        // Ultimate fallback — route to default provider; local will fail explicitly if unreachable
        return BuildDecision(_options.DefaultProvider, _options.DefaultModel, "ultimate-fallback", strategy);
    }

    private ModelRouteDecision? TryAdaptiveRoute(string strategy)
    {
        var bestModel = _performanceTracker.GetBestModelForStrategy(strategy);
        if (bestModel is null) return null;

        return BuildDecision(
            bestModel.Provider,
            bestModel.Model,
            $"adaptive-route:{strategy}:score-{bestModel.CompositeScore:F3}",
            strategy);
    }

    private ModelRouteDecision RouteByStrategy(string strategy)
    {
        return strategy switch
        {
            "cost" => BuildDecision(_options.CostOptimizedProvider, _options.CostOptimizedModel, "cost-optimization", strategy),
            "latency" => BuildDecision(_options.LatencyOptimizedProvider, _options.LatencyOptimizedModel, "latency-optimization", strategy),
            "quality" => BuildDecision(_options.QualityOptimizedProvider, _options.QualityOptimizedModel, "quality-optimization", strategy),
            _ => BuildDecision(_options.DefaultProvider, _options.DefaultModel, "default-route", strategy)
        };
    }

    private static string ResolveStrategy(ModelRequest request)
    {
        if (request.Parameters.TryGetValue("optimize", out string? optimize) && !string.IsNullOrWhiteSpace(optimize))
        {
            return optimize.ToLowerInvariant();
        }
        return "default";
    }

    private static ModelRouteDecision BuildDecision(string provider, string model, string reason, string strategy)
    {
        return new ModelRouteDecision(
            Provider: provider,
            Model: model,
            Reason: reason,
            CostOptimized: strategy == "cost",
            LatencyOptimized: strategy == "latency",
            RoutedAtUtc: DateTimeOffset.UtcNow);
    }

    private static string ResolveProviderFromModel(string model)
    {
        if (model.StartsWith("openai", StringComparison.OrdinalIgnoreCase))
            return "openai";

        if (model.StartsWith("azure", StringComparison.OrdinalIgnoreCase))
            return "azure-openai";

        if (model.StartsWith("anthropic", StringComparison.OrdinalIgnoreCase))
            return "anthropic";

        return "local";
    }
}

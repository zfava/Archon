using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Optimization;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIOptimization(this IServiceCollection services)
    {
        services.AddOptions<OptimizationOptions>()
            .BindConfiguration("Optimization");

        services.AddSingleton<IPerformanceAnalyzer, PerformanceAnalyzer>();
        services.AddSingleton<IOptimizationEngine, OptimizationEngine>();
        return services;
    }
}

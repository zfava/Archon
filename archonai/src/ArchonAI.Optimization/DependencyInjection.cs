using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Optimization;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIOptimization(this IServiceCollection services)
    {
        services.AddOptions<OptimizationOptions>()
            .BindConfiguration("Optimization");

        services.AddSingleton<IOptimizationEngine, OptimizationEngine>();
        return services;
    }
}

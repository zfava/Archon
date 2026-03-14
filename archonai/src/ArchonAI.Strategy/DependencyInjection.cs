using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Strategy;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIStrategy(this IServiceCollection services)
    {
        services.AddOptions<StrategyOptions>()
            .BindConfiguration("Strategy");

        services.AddSingleton<IStrategyStore, InMemoryStrategyStore>();
        return services;
    }
}

using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.PatternDiscovery;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIPatternDiscovery(this IServiceCollection services)
    {
        services.AddOptions<PatternDiscoveryOptions>()
            .BindConfiguration("PatternDiscovery");

        services.AddSingleton<IPatternDiscoveryEngine, PatternDiscoveryEngine>();
        return services;
    }
}

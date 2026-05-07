using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Perception;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIPerception(this IServiceCollection services)
    {
        services.AddOptions<PerceptionOptions>()
            .BindConfiguration("Perception");

        services.AddSingleton<IPerceptionEngine, PerceptionEngine>();
        services.AddSingleton<IBusinessPerceptionEngine, BusinessPerceptionEngine>();
        services.AddSingleton<ISignalIngestionService, SignalIngestionService>();

        return services;
    }
}

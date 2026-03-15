using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Simulation;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAISimulation(this IServiceCollection services)
    {
        services.AddOptions<SimulationOptions>()
            .BindConfiguration("Simulation");

        services.AddSingleton<ISimulationEngine, SimulationEngine>();
        return services;
    }
}

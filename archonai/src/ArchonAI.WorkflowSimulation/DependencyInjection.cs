using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.WorkflowSimulation;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIWorkflowSimulation(
        this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<SimulationEngineOptions>(
                configuration.GetSection(SimulationEngineOptions.SectionName));
        }
        else
        {
            services.Configure<SimulationEngineOptions>(_ => { });
        }

        services.AddSingleton<IWorkflowSimulationEngine, SimulationEngine>();
        services.AddSingleton<IWorkflowSimulationService, SimulationService>();
        return services;
    }
}

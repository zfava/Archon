using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models.Simulation;
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
            services.Configure<StrategySimulatorOptions>(
                configuration.GetSection(StrategySimulatorOptions.SectionName));
        }
        else
        {
            services.Configure<SimulationEngineOptions>(_ => { });
            services.Configure<StrategySimulatorOptions>(_ => { });
        }

        services.AddSingleton<IWorkflowSimulationEngine, SimulationEngine>();
        services.AddSingleton<IWorkflowSimulationService, SimulationService>();
        services.AddSingleton<IStrategySimulator, StrategySimulator>();
        return services;
    }
}

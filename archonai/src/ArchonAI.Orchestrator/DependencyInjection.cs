using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Orchestrator;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIOrchestrator(this IServiceCollection services)
    {
        services.AddOptions<OrchestratorOptions>()
            .BindConfiguration("Orchestrator");

        services.AddSingleton<IDistributedTaskOrchestrator, DistributedTaskOrchestrator>();
        return services;
    }
}

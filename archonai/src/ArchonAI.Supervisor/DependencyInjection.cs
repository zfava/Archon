using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Supervisor;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAISupervisor(this IServiceCollection services)
    {
        services.AddOptions<SupervisorOptions>()
            .BindConfiguration("Supervisor");

        services.AddSingleton<IAgentSupervisor, AgentSupervisor>();
        return services;
    }
}

using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.AgentRegistry;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIAgentRegistry(this IServiceCollection services)
    {
        services.AddSingleton<IAgentRegistryRepository, AgentRepository>();
        services.AddSingleton<IAgentRegistryService, AgentRegistryService>();
        return services;
    }
}

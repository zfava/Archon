using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Registry;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIRegistry(this IServiceCollection services)
    {
        services.AddSingleton<IAgentCapabilityRegistry, InMemoryAgentCapabilityRegistry>();
        return services;
    }
}

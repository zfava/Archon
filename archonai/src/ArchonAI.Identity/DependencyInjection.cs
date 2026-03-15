using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Identity;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIIdentity(this IServiceCollection services)
    {
        services.AddOptions<IdentityOptions>()
            .BindConfiguration("Identity");

        services.AddSingleton<IAgentIdentityStore, InMemoryAgentIdentityStore>();
        return services;
    }
}

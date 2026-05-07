using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.OrganizationState;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIOrganizationState(this IServiceCollection services)
    {
        services.AddSingleton<IOrganizationStateEngine, OrganizationStateEngine>();
        services.AddHostedService<OrganizationStateSignalListener>();

        return services;
    }
}

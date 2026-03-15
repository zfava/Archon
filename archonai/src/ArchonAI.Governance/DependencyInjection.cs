using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Governance;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIGovernance(this IServiceCollection services)
    {
        services.AddOptions<GovernanceOptions>()
            .BindConfiguration("Governance");

        services.AddSingleton<IGovernanceKernel, GovernanceKernel>();
        return services;
    }
}

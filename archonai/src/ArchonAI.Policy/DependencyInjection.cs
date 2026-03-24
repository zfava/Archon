using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Policy;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIPolicy(this IServiceCollection services)
    {
        services.AddOptions<PolicyOptions>()
            .BindConfiguration("Policy");

        services.AddSingleton<IPolicyEngine, PolicyEngine>();
        return services;
    }
}

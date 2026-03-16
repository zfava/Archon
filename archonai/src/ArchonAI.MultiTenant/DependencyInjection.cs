using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.MultiTenant;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIMultiTenant(this IServiceCollection services)
    {
        services.AddOptions<MultiTenantOptions>()
            .BindConfiguration("MultiTenant");

        services.AddSingleton<IMultiTenantContext, MultiTenantContext>();
        services.AddSingleton<ITenantResourceGovernor, TenantResourceGovernor>();

        return services;
    }
}

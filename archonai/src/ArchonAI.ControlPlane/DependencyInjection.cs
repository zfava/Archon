using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.ControlPlane;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIControlPlane(
        this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<ControlPlaneOptions>(
                configuration.GetSection(ControlPlaneOptions.SectionName));
        }
        else
        {
            services.Configure<ControlPlaneOptions>(_ => { });
        }

        services.AddSingleton<IControlPlaneRepository, InMemoryControlPlaneRepository>();
        services.AddSingleton<IControlPlaneService, ControlPlaneService>();

        return services;
    }
}

using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Agents.Sales;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAISales(this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<SalesOptions>(configuration.GetSection(SalesOptions.SectionName));
        }
        else
        {
            services.Configure<SalesOptions>(_ => { });
        }

        services.AddSingleton<ISalesEngine, SalesEngine>();
        services.AddSingleton<IAgent, SalesAgent>();

        return services;
    }
}

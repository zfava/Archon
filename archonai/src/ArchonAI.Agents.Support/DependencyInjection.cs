using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Agents.Support;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAISupport(this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<SupportOptions>(configuration.GetSection(SupportOptions.SectionName));
        }
        else
        {
            services.Configure<SupportOptions>(_ => { });
        }

        services.AddSingleton<ISupportEngine, SupportEngine>();
        services.AddSingleton<IAgent, SupportAgent>();

        return services;
    }
}

using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Agents.Marketing;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIMarketing(this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<MarketingOptions>(configuration.GetSection(MarketingOptions.SectionName));
        }
        else
        {
            services.Configure<MarketingOptions>(_ => { });
        }

        services.AddSingleton<IMarketingEngine, MarketingEngine>();
        services.AddSingleton<IAgent, MarketingAgent>();

        return services;
    }
}

using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Agents.Finance;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIFinance(this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<FinanceOptions>(configuration.GetSection(FinanceOptions.SectionName));
        }
        else
        {
            services.Configure<FinanceOptions>(_ => { });
        }

        services.AddSingleton<IFinanceEngine, FinanceEngine>();
        services.AddSingleton<IAgent, FinanceAgent>();

        return services;
    }
}

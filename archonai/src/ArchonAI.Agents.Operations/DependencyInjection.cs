using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Agents.Operations;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIOperations(this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<OperationsOptions>(configuration.GetSection(OperationsOptions.SectionName));
        }
        else
        {
            services.Configure<OperationsOptions>(_ => { });
        }

        services.AddSingleton<IOperationsEngine, OperationsEngine>();
        services.AddSingleton<IAgent, OperationsAgent>();

        return services;
    }
}

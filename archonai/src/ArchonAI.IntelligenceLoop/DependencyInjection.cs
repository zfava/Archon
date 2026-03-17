using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.IntelligenceLoop;

public static class DependencyInjection
{
    public static IServiceCollection AddIntelligenceLoop(this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<IntelligenceLoopOptions>(configuration.GetSection(IntelligenceLoopOptions.SectionName));
        }
        else
        {
            services.Configure<IntelligenceLoopOptions>(_ => { });
        }

        services.AddSingleton<IIntelligenceLoop, IntelligenceLoopOrchestrator>();
        services.AddHostedService<IntelligenceLoopHostedService>();

        return services;
    }
}

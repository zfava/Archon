using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Scheduler;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIScheduler(this IServiceCollection services)
    {
        services.AddOptions<SchedulerOptions>()
            .BindConfiguration("Scheduler");

        services.AddSingleton<IResourceScheduler, ResourceScheduler>();
        return services;
    }
}

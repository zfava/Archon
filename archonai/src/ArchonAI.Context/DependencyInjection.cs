using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Context;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIContext(this IServiceCollection services)
    {
        services.AddOptions<ContextOptions>()
            .BindConfiguration("Context");

        services.AddSingleton<IContextEngine, ContextEngine>();
        return services;
    }
}

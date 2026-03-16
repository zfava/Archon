using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.ModelRouter;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIModelRouter(this IServiceCollection services)
    {
        services.AddOptions<ModelRouterOptions>()
            .BindConfiguration("ModelRouter");

        services.AddSingleton<IModelRouter, ModelRouter>();
        return services;
    }
}

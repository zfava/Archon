using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.StrategyLibrary;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIStrategyLibrary(this IServiceCollection services)
    {
        services.AddSingleton<IStrategyLibraryRepository, StrategyRepository>();
        services.AddSingleton<IStrategyLibraryService, StrategyService>();
        return services;
    }
}

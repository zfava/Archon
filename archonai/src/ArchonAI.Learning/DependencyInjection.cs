using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Learning;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAILearning(this IServiceCollection services)
    {
        services.AddOptions<LearningOptions>()
            .BindConfiguration("Learning");

        services.AddSingleton<ILearningEngine, LearningEngine>();
        services.AddSingleton<IStrategyLearningEngine, StrategyLearningEngine>();
        return services;
    }
}

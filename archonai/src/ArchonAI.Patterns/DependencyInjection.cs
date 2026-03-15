using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Patterns;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIPatterns(this IServiceCollection services)
    {
        services.AddOptions<PatternOptions>()
            .BindConfiguration("Patterns");

        services.AddSingleton<IPatternAnalyzer, PatternAnalyzer>();
        return services;
    }
}

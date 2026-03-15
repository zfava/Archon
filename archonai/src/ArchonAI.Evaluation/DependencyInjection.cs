using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.Evaluation;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIEvaluation(this IServiceCollection services)
    {
        services.AddSingleton<IEvaluationEngine, EvaluationEngine>();
        return services;
    }
}

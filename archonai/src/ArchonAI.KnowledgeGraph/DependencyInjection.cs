using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ArchonAI.KnowledgeGraph;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIKnowledgeGraph(this IServiceCollection services)
    {
        services.AddSingleton<IKnowledgeGraphEngine, KnowledgeGraphEngine>();
        return services;
    }
}

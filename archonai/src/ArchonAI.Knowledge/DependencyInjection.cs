using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArchonAI.Knowledge;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAIKnowledge(this IServiceCollection services)
    {
        services.AddOptions<KnowledgeGraphOptions>()
            .BindConfiguration("KnowledgeGraph");

        services.AddSingleton<InMemoryKnowledgeGraphStore>();
        services.AddSingleton<PostgresKnowledgeGraphStore>();
        services.AddSingleton<IKnowledgeGraphStore>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<KnowledgeGraphOptions>>().Value;
            return string.IsNullOrWhiteSpace(options.ConnectionString)
                ? serviceProvider.GetRequiredService<InMemoryKnowledgeGraphStore>()
                : serviceProvider.GetRequiredService<PostgresKnowledgeGraphStore>();
        });

        return services;
    }
}

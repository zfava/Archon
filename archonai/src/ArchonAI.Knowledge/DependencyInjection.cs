using ArchonAI.Common;
using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
            if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                return serviceProvider.GetRequiredService<PostgresKnowledgeGraphStore>();
            }

            // ── Environment-aware fallback gate ──────────────────────────
            var posture = serviceProvider.GetService<EnvironmentPosture>();
            var logger = serviceProvider.GetRequiredService<ILogger<KnowledgeGraphOptions>>();
            posture?.GuardInMemoryFallback(
                "KnowledgeGraph",
                "KnowledgeGraph:ConnectionString",
                logger);
            return serviceProvider.GetRequiredService<InMemoryKnowledgeGraphStore>();
        });

        return services;
    }
}

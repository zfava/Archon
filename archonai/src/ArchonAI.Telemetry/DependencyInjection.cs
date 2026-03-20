using ArchonAI.Common;
using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Telemetry;

public static class DependencyInjection
{
    public static IServiceCollection AddArchonAITelemetry(this IServiceCollection services)
    {
        services.AddOptions<TelemetryOptions>()
            .BindConfiguration("TelemetryPersistence");

        services.AddSingleton<PostgresTaskTelemetryStore>();
        services.AddSingleton<InMemoryTaskTelemetryStore>();

        services.AddSingleton<ITaskTelemetryStore>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<TelemetryOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                return serviceProvider.GetRequiredService<PostgresTaskTelemetryStore>();
            }

            // ── Environment-aware fallback gate ──────────────────────────
            var posture = serviceProvider.GetService<EnvironmentPosture>();
            var logger = serviceProvider.GetRequiredService<ILogger<TelemetryOptions>>();
            posture?.GuardInMemoryFallback(
                "Telemetry",
                "TelemetryPersistence:ConnectionString",
                logger);
            return serviceProvider.GetRequiredService<InMemoryTaskTelemetryStore>();
        });

        services.AddSingleton<ISystemInsightEngine, SystemInsightEngine>();

        return services;
    }
}

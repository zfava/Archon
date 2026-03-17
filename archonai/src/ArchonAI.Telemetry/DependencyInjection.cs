using ArchonAI.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
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
            return string.IsNullOrWhiteSpace(options.ConnectionString)
                ? serviceProvider.GetRequiredService<InMemoryTaskTelemetryStore>()
                : serviceProvider.GetRequiredService<PostgresTaskTelemetryStore>();
        });

        services.AddSingleton<ISystemInsightEngine, SystemInsightEngine>();

        return services;
    }
}

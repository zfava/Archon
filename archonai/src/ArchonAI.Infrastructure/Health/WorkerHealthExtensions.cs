using ArchonAI.Infrastructure.Eventing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client;

namespace ArchonAI.Infrastructure.Health;

/// <summary>
/// Extension methods to wire up worker health endpoints.
/// </summary>
public static class WorkerHealthExtensions
{
    /// <summary>
    /// Adds a lightweight /healthz/live and /healthz/ready endpoint on port 8081.
    /// Readiness checks: NATS connected, event bus accessible.
    /// </summary>
    public static IServiceCollection AddWorkerHealthEndpoint(
        this IServiceCollection services,
        string workerRole,
        int port = 8081)
    {
        services.AddHostedService(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<WorkerHealthService>>();
            var eventBusOptions = sp.GetRequiredService<IOptions<EventBusOptions>>().Value;

            Func<CancellationToken, Task<HealthProbeResult>> readinessCheck = async ct =>
            {
                var checks = new Dictionary<string, HealthCheckEntry>();
                bool allOk = true;

                // Check NATS connection if NATS is enabled
                if (eventBusOptions.UseNats)
                {
                    try
                    {
                        var factory = new ConnectionFactory();
                        using var conn = factory.CreateConnection(eventBusOptions.Url);
                        bool connected = conn.State == ConnState.CONNECTED;
                        checks["nats"] = new HealthCheckEntry
                        {
                            Status = connected ? "healthy" : "unhealthy",
                            Detail = connected ? $"Connected to {eventBusOptions.Url}" : "Not connected"
                        };
                        if (!connected) allOk = false;
                        conn.Close();
                    }
                    catch (Exception ex)
                    {
                        checks["nats"] = new HealthCheckEntry { Status = "unhealthy", Detail = ex.Message };
                        allOk = false;
                    }
                }
                else
                {
                    checks["event_bus"] = new HealthCheckEntry { Status = "healthy", Detail = "In-memory event bus" };
                }

                checks["worker_role"] = new HealthCheckEntry { Status = "healthy", Detail = workerRole };

                return new HealthProbeResult { IsReady = allOk, Checks = checks };
            };

            return new WorkerHealthService(logger, readinessCheck, port);
        });

        return services;
    }
}

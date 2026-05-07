using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchonAI.Migrations;

/// <summary>
/// Health check that verifies all database migrations have been applied.
/// Reports Unhealthy if pending migrations exist or if the database is unreachable.
/// </summary>
public sealed class MigrationHealthCheck : IHealthCheck
{
    private readonly MigrationRunner _runner;
    private readonly ILogger<MigrationHealthCheck> _logger;

    public MigrationHealthCheck(MigrationRunner runner, ILogger<MigrationHealthCheck> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pending = _runner.GetPendingMigrations();

            if (pending.Count == 0)
            {
                return Task.FromResult(HealthCheckResult.Healthy("All database migrations applied."));
            }

            var message = $"{pending.Count} pending migration(s): {string.Join(", ", pending.Take(5))}";
            _logger.LogWarning("Migration health check: {Message}", message);

            return Task.FromResult(HealthCheckResult.Unhealthy(message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Cannot verify migration status",
                ex));
        }
    }
}

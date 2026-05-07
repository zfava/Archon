using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArchonAI.Migrations;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the MigrationRunner and MigrationHealthCheck.
    /// Reads the connection string from ArchonAIPersistence:ConnectionString
    /// (same config used by the persistence stores).
    /// </summary>
    public static IServiceCollection AddArchonAIMigrations(this IServiceCollection services)
    {
        services.AddOptions<MigrationOptions>()
            .BindConfiguration(MigrationOptions.SectionName);

        services.AddSingleton<MigrationRunner>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MigrationOptions>>().Value;
            var connectionString = options.ConnectionString;

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Database migration connection string is not configured. " +
                    $"Set {MigrationOptions.SectionName}:ConnectionString.");
            }

            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MigrationRunner>>();
            return new MigrationRunner(connectionString, logger);
        });

        services.AddSingleton<MigrationHealthCheck>();

        return services;
    }
}

public sealed class MigrationOptions
{
    public const string SectionName = "ArchonAIPersistence";

    public string? ConnectionString { get; set; }
}

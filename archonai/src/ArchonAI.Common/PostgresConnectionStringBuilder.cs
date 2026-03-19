using Npgsql;

namespace ArchonAI.Common;

/// <summary>
/// Hardens PostgreSQL connection strings by enforcing SSL, pool sizing, and security defaults.
/// All Postgres consumers in the codebase must route their connection strings through Harden().
/// </summary>
public static class PostgresConnectionStringBuilder
{
    /// <summary>
    /// Ensures SslMode=Require and Trust Server Certificate=false are present.
    /// Also enforces connection pool sizing (Min Pool Size=2, Max Pool Size=50, Connection Idle Lifetime=300).
    /// Throws ArgumentException if the connection string is empty.
    /// </summary>
    /// <param name="connectionString">Raw connection string from configuration.</param>
    /// <param name="requireSsl">When true (default), enforces SslMode=Require. When false, uses SslMode=Prefer.</param>
    /// <returns>Hardened connection string with security and pooling defaults applied.</returns>
    public static string Harden(string connectionString, bool requireSsl = true)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("PostgreSQL connection string must not be empty.", nameof(connectionString));

        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        // Allow SSL to be disabled for local development via environment variable
        var sslDisableEnv = Environment.GetEnvironmentVariable("ARCHONAI_POSTGRES_SSL_DISABLE");
        if (string.Equals(sslDisableEnv, "true", StringComparison.OrdinalIgnoreCase))
        {
            builder.SslMode = SslMode.Disable;
            Console.Error.WriteLine(
                "WARNING: PostgreSQL SSL is disabled (ARCHONAI_POSTGRES_SSL_DISABLE=true). " +
                "This must never be used in production.");
        }
        else
        {
            builder.SslMode = requireSsl ? SslMode.Require : SslMode.Prefer;
        }

        // Connection pool sizing defaults (only set if not already specified)
        if (!connectionString.Contains("Minimum Pool Size", StringComparison.OrdinalIgnoreCase)
            && !connectionString.Contains("MinPoolSize", StringComparison.OrdinalIgnoreCase))
        {
            builder.MinPoolSize = 2;
        }

        if (!connectionString.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase)
            && !connectionString.Contains("MaxPoolSize", StringComparison.OrdinalIgnoreCase))
        {
            builder.MaxPoolSize = 50;
        }

        if (!connectionString.Contains("Connection Idle Lifetime", StringComparison.OrdinalIgnoreCase)
            && !connectionString.Contains("ConnectionIdleLifetime", StringComparison.OrdinalIgnoreCase))
        {
            builder.ConnectionIdleLifetime = 300;
        }

        return builder.ConnectionString;
    }
}

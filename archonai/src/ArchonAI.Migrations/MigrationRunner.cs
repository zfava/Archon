using System.Diagnostics;
using System.Reflection;
using ArchonAI.Common;
using DbUp;
using DbUp.Engine;
using Microsoft.Extensions.Logging;

namespace ArchonAI.Migrations;

/// <summary>
/// Runs versioned PostgreSQL migrations using DbUp with embedded SQL scripts.
/// Scripts are executed in alphabetical order and tracked in the schemaversions
/// journal table so each migration runs exactly once.
/// </summary>
public sealed class MigrationRunner
{
    private readonly string _connectionString;
    private readonly ILogger<MigrationRunner>? _logger;

    public MigrationRunner(string connectionString, ILogger<MigrationRunner>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = PostgresConnectionStringBuilder.Harden(connectionString);
        _logger = logger;
    }

    /// <summary>
    /// Execute all pending migrations. Returns true if all migrations succeeded.
    /// </summary>
    public MigrationResult Run()
    {
        var sw = Stopwatch.StartNew();
        _logger?.LogInformation("Starting database migration...");

        // Ensure the database exists (DbUp helper)
        EnsureDatabase.For.PostgresqlDatabase(_connectionString);

        var upgrader = DeployChanges.To
            .PostgresqlDatabase(_connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                s => s.Contains(".Scripts."))
            .WithTransactionPerScript()
            .LogToConsole()
            .Build();

        var pendingScripts = upgrader.GetScriptsToExecute();
        _logger?.LogInformation("Found {Count} pending migration(s)", pendingScripts.Count);

        foreach (var script in pendingScripts)
        {
            _logger?.LogInformation("  Pending: {ScriptName}", script.Name);
        }

        var result = upgrader.PerformUpgrade();
        sw.Stop();

        if (!result.Successful)
        {
            _logger?.LogError(result.Error,
                "Migration FAILED after {ElapsedMs}ms on script: {Script}",
                sw.ElapsedMilliseconds,
                result.ErrorScript?.Name ?? "(unknown)");

            return new MigrationResult(
                Success: false,
                ElapsedMs: sw.ElapsedMilliseconds,
                ScriptsExecuted: result.Scripts.Select(s => s.Name).ToList(),
                Error: result.Error?.Message,
                FailedScript: result.ErrorScript?.Name);
        }

        var scriptCount = result.Scripts.Count();
        _logger?.LogInformation(
            "Migration completed successfully in {ElapsedMs}ms. {Count} script(s) executed.",
            (object)sw.ElapsedMilliseconds,
            (object)scriptCount);

        return new MigrationResult(
            Success: true,
            ElapsedMs: sw.ElapsedMilliseconds,
            ScriptsExecuted: result.Scripts.Select(s => s.Name).ToList(),
            Error: null,
            FailedScript: null);
    }

    /// <summary>
    /// Returns the list of scripts that have not yet been applied.
    /// </summary>
    public IReadOnlyList<string> GetPendingMigrations()
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(_connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                s => s.Contains(".Scripts."))
            .Build();

        return upgrader.GetScriptsToExecute()
            .Select(s => s.Name)
            .ToList();
    }

    /// <summary>
    /// Returns true if all embedded migrations have been applied.
    /// </summary>
    public bool IsUpToDate()
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(_connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                s => s.Contains(".Scripts."))
            .Build();

        return !upgrader.IsUpgradeRequired();
    }
}

public sealed record MigrationResult(
    bool Success,
    long ElapsedMs,
    IReadOnlyList<string> ScriptsExecuted,
    string? Error,
    string? FailedScript);

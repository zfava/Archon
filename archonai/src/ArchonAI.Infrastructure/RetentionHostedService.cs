using System.Diagnostics;
using ArchonAI.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArchonAI.Infrastructure;

public sealed class RetentionHostedService : BackgroundService
{
    private readonly PersistenceOptions _persistenceOptions;
    private readonly ILogger<RetentionHostedService> _logger;

    public RetentionHostedService(
        IOptions<PersistenceOptions> persistenceOptions,
        ILogger<RetentionHostedService> logger)
    {
        _persistenceOptions = persistenceOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_persistenceOptions.Retention.EnableAutoRetention)
        {
            _logger.LogInformation("Auto-retention is disabled. RetentionHostedService will not run");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun();
            _logger.LogDebug("Next retention sweep in {Delay}", delay);
            await Task.Delay(delay, stoppingToken);

            await RunSweepAsync(stoppingToken);
        }
    }

    public async Task<RetentionSweepResult> RunSweepAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var retention = _persistenceOptions.Retention;
        var schema = _persistenceOptions.Schema;
        var connStr = _persistenceOptions.ConnectionStringHardened;

        if (string.IsNullOrWhiteSpace(connStr))
        {
            _logger.LogWarning("Retention sweep skipped: no persistence connection string configured");
            return new RetentionSweepResult(0, 0, 0, 0);
        }

        long auditDeleted = 0, telemetryDeleted = 0, memoryExpired = 0;

        try
        {
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync(ct);

            auditDeleted = await DeleteOlderThanAsync(
                conn, $"{schema}.audit_log", "occurred_at_utc", retention.AuditLogRetentionDays, ct);

            telemetryDeleted = await DeleteOlderThanAsync(
                conn, $"{schema}.task_telemetry", "recorded_at_utc", retention.TelemetryRetentionDays, ct);

            memoryExpired = await DeleteOlderThanAsync(
                conn, $"{schema}.enterprise_memory", "created_at_utc", retention.EnterpriseMemorySessionRetentionHours,
                ct, isHours: true, extraCondition: "AND layer = 'Session'");

            sw.Stop();

            await RecordSweepAsync(conn, schema, auditDeleted, 0, telemetryDeleted, sw.ElapsedMilliseconds, ct);

            _logger.LogInformation(
                "Retention sweep completed. AuditLog: {AuditDeleted} rows, Traces: {TraceDeleted} rows, Telemetry: {TelemetryDeleted} rows, Memory: {MemoryExpired} rows, Duration: {DurationMs}ms",
                auditDeleted, 0, telemetryDeleted, memoryExpired, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retention sweep failed after {DurationMs}ms", sw.ElapsedMilliseconds);
        }

        return new RetentionSweepResult(auditDeleted, 0, telemetryDeleted, sw.ElapsedMilliseconds);
    }

    private static async Task<long> DeleteOlderThanAsync(
        NpgsqlConnection conn, string table, string timestampColumn, int retentionUnits,
        CancellationToken ct, bool isHours = false, string? extraCondition = null)
    {
        var cutoff = isHours
            ? DateTimeOffset.UtcNow.AddHours(-retentionUnits)
            : DateTimeOffset.UtcNow.AddDays(-retentionUnits);

        var sql = $"DELETE FROM {table} WHERE {timestampColumn} < @cutoff {extraCondition}";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("cutoff", cutoff.UtcDateTime);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task RecordSweepAsync(
        NpgsqlConnection conn, string schema,
        long auditRows, long traceRows, long telemetryRows, long durationMs,
        CancellationToken ct)
    {
        try
        {
            var sql = $"""
                INSERT INTO {schema}.retention_log
                    (id, ran_at_utc, audit_rows_deleted, trace_rows_deleted, telemetry_rows_deleted, duration_ms)
                VALUES
                    (@id, @ranAtUtc, @auditRows, @traceRows, @telemetryRows, @durationMs)
                """;

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("ranAtUtc", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("auditRows", auditRows);
            cmd.Parameters.AddWithValue("traceRows", traceRows);
            cmd.Parameters.AddWithValue("telemetryRows", telemetryRows);
            cmd.Parameters.AddWithValue("durationMs", durationMs);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception)
        {
            // retention_log table may not exist yet if migration hasn't run
        }
    }

    private static TimeSpan TimeUntilNextRun()
    {
        var now = DateTimeOffset.UtcNow;
        var nextRun = now.Date.AddDays(1).AddHours(2); // 02:00 UTC tomorrow
        if (now.Hour < 2)
            nextRun = now.Date.AddHours(2); // 02:00 UTC today if we haven't passed it

        return nextRun - now;
    }
}

public sealed record RetentionSweepResult(
    long AuditRowsDeleted,
    long TraceRowsDeleted,
    long TelemetryRowsDeleted,
    long DurationMs);

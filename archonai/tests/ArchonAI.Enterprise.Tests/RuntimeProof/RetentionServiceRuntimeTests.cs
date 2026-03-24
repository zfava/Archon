using Npgsql;
using Testcontainers.PostgreSql;
using ArchonAI.Infrastructure;
using ArchonAI.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ArchonAI.Enterprise.Tests.RuntimeProof;

/// <summary>
/// Runtime-proof tests for the <see cref="RetentionHostedService"/>.
/// Uses a real PostgreSQL container to prove that the retention sweep:
///   1. Correctly deletes rows older than the configured retention period
///   2. Preserves rows within the retention window
///   3. Records sweep results in the retention_log table
///   4. Handles empty tables gracefully
///   5. Handles missing connection string gracefully
///
/// These tests exercise the actual production SQL paths in RetentionHostedService.RunSweepAsync().
/// </summary>
[Trait("Category", "RuntimeProof")]
[Trait("Subsystem", "Retention")]
[Trait("Database", "PostgreSQL")]
public sealed class RetentionServiceRuntimeTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("archonai_retention_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private string _connectionString = default!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();

        Environment.SetEnvironmentVariable("ARCHONAI_POSTGRES_SSL_DISABLE", "true");

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(RetentionDdl, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    private RetentionHostedService CreateService(int auditRetentionDays = 90, int telemetryRetentionDays = 30, int memorySessionRetentionHours = 24)
    {
        var options = Options.Create(new PersistenceOptions
        {
            ConnectionString = _connectionString,
            Schema = "archonai",
            Retention = new RetentionOptions
            {
                AuditLogRetentionDays = auditRetentionDays,
                TelemetryRetentionDays = telemetryRetentionDays,
                EnterpriseMemorySessionRetentionHours = memorySessionRetentionHours,
                EnableAutoRetention = true,
            }
        });

        return new RetentionHostedService(options, NullLogger<RetentionHostedService>.Instance);
    }

    // ── Test 1: Sweep deletes expired audit log rows ────────────────────

    [Fact]
    public async Task Sweep_DeletesExpiredAuditRows_PreservesRecent()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // Insert 3 old rows (200 days old) and 2 recent rows (5 days old)
        for (int i = 0; i < 3; i++)
            await InsertAuditRow(conn, DateTimeOffset.UtcNow.AddDays(-200));
        for (int i = 0; i < 2; i++)
            await InsertAuditRow(conn, DateTimeOffset.UtcNow.AddDays(-5));

        var service = CreateService(auditRetentionDays: 90);
        var result = await service.RunSweepAsync();

        Assert.Equal(3, result.AuditRowsDeleted);

        // Verify 2 recent rows remain
        await using var countCmd = new NpgsqlCommand("SELECT COUNT(*) FROM archonai.audit_log", conn);
        var remaining = Convert.ToInt64(await countCmd.ExecuteScalarAsync());
        Assert.Equal(2, remaining);
    }

    // ── Test 2: Sweep deletes expired telemetry rows ────────────────────

    [Fact]
    public async Task Sweep_DeletesExpiredTelemetryRows_PreservesRecent()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // Insert 4 old rows (60 days) and 1 recent (2 days)
        for (int i = 0; i < 4; i++)
            await InsertTelemetryRow(conn, DateTimeOffset.UtcNow.AddDays(-60));
        await InsertTelemetryRow(conn, DateTimeOffset.UtcNow.AddDays(-2));

        var service = CreateService(telemetryRetentionDays: 30);
        var result = await service.RunSweepAsync();

        Assert.Equal(4, result.TelemetryRowsDeleted);

        await using var countCmd = new NpgsqlCommand("SELECT COUNT(*) FROM archonai.task_telemetry", conn);
        var remaining = Convert.ToInt64(await countCmd.ExecuteScalarAsync());
        Assert.Equal(1, remaining);
    }

    // ── Test 3: Sweep handles empty tables ──────────────────────────────

    [Fact]
    public async Task Sweep_EmptyTables_CompletesWithZeroDeletes()
    {
        var service = CreateService();
        var result = await service.RunSweepAsync();

        Assert.Equal(0, result.AuditRowsDeleted);
        Assert.Equal(0, result.TelemetryRowsDeleted);
        Assert.True(result.DurationMs >= 0);
    }

    // ── Test 4: Sweep records to retention_log ──────────────────────────

    [Fact]
    public async Task Sweep_RecordsSweepResult_InRetentionLog()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // Insert one old audit row to make the sweep do something
        await InsertAuditRow(conn, DateTimeOffset.UtcNow.AddDays(-200));

        var service = CreateService(auditRetentionDays: 90);
        await service.RunSweepAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM archonai.retention_log", conn);
        var logCount = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        Assert.True(logCount >= 1, "Retention sweep should have recorded at least one log entry");
    }

    // ── Test 5: No-op when connection string is empty ───────────────────

    [Fact]
    public async Task Sweep_NoConnectionString_ReturnsZeroResult()
    {
        var options = Options.Create(new PersistenceOptions
        {
            ConnectionString = null,
            Schema = "archonai",
            Retention = new RetentionOptions { EnableAutoRetention = true },
        });
        var service = new RetentionHostedService(options, NullLogger<RetentionHostedService>.Instance);

        var result = await service.RunSweepAsync();

        Assert.Equal(0, result.AuditRowsDeleted);
        Assert.Equal(0, result.TelemetryRowsDeleted);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static async Task InsertAuditRow(NpgsqlConnection conn, DateTimeOffset occurredAt)
    {
        var sql = """
            INSERT INTO archonai.audit_log
                (id, event_type, category, source, subject_id, subject_type, action, resource_type, resource_id, description, checksum, occurred_at_utc)
            VALUES
                (@id, 'test', 'test', 'test', 'test', 'test', 'test', 'test', 'test', 'test', 'abc', @ts)
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("ts", occurredAt.UtcDateTime);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertTelemetryRow(NpgsqlConnection conn, DateTimeOffset recordedAt)
    {
        var sql = """
            INSERT INTO archonai.task_telemetry
                (id, objective_id, task_id, recorded_at_utc)
            VALUES
                (@id, @objId, @taskId, @ts)
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("objId", Guid.NewGuid());
        cmd.Parameters.AddWithValue("taskId", Guid.NewGuid());
        cmd.Parameters.AddWithValue("ts", recordedAt.UtcDateTime);
        await cmd.ExecuteNonQueryAsync();
    }

    // Minimal DDL for the tables that RetentionHostedService targets.
    private const string RetentionDdl = """
        CREATE SCHEMA IF NOT EXISTS archonai;

        CREATE TABLE IF NOT EXISTS archonai.audit_log (
            id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            event_type      text NOT NULL,
            category        text NOT NULL,
            source          text NOT NULL,
            subject_id      text NOT NULL,
            subject_type    text NOT NULL,
            action          text NOT NULL,
            resource_type   text NOT NULL,
            resource_id     text NOT NULL,
            description     text NOT NULL,
            metadata        jsonb NOT NULL DEFAULT '{}'::jsonb,
            checksum        text NOT NULL,
            previous_entry_id uuid,
            occurred_at_utc timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS archonai.task_telemetry (
            id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            objective_id    uuid NOT NULL,
            task_id         uuid NOT NULL,
            recorded_at_utc timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS archonai.enterprise_memory (
            id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            layer           text NOT NULL,
            created_at_utc  timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS archonai.retention_log (
            id              uuid PRIMARY KEY,
            ran_at_utc      timestamptz NOT NULL,
            audit_rows_deleted   bigint NOT NULL DEFAULT 0,
            trace_rows_deleted   bigint NOT NULL DEFAULT 0,
            telemetry_rows_deleted bigint NOT NULL DEFAULT 0,
            inspection_rows_deleted bigint NOT NULL DEFAULT 0,
            duration_ms     bigint NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS archonai.inspection_policy_evaluations (
            evaluation_id    uuid             PRIMARY KEY DEFAULT gen_random_uuid(),
            tenant_id        uuid             NOT NULL,
            subject_type     text             NOT NULL,
            subject_id       text             NOT NULL,
            is_allowed       boolean          NOT NULL,
            risk_score       double precision NOT NULL,
            confidence_score double precision NOT NULL,
            requires_approval boolean         NOT NULL,
            approval_state   text             NOT NULL,
            manual_override_state text        NOT NULL,
            approval_checkpoint text          NOT NULL,
            guardrail_violations jsonb        NOT NULL DEFAULT '[]'::jsonb,
            rules_evaluated  jsonb            NOT NULL DEFAULT '[]'::jsonb,
            reason           text             NOT NULL,
            evaluated_at_utc timestamptz      NOT NULL DEFAULT now()
        );

        CREATE TABLE IF NOT EXISTS archonai.inspection_memory_references (
            id              uuid             PRIMARY KEY DEFAULT gen_random_uuid(),
            tenant_id       uuid             NOT NULL,
            subject_type    text             NOT NULL,
            subject_id      text             NOT NULL,
            memory_id       uuid             NOT NULL,
            memory_type     text             NOT NULL,
            source          text             NOT NULL,
            content_summary text             NOT NULL,
            relevance_score double precision NOT NULL,
            usage_context   text             NOT NULL,
            retrieved_at_utc timestamptz     NOT NULL DEFAULT now()
        );

        CREATE TABLE IF NOT EXISTS archonai.inspection_workflow_diagnostics (
            workflow_id      uuid        PRIMARY KEY,
            tenant_id        uuid        NOT NULL,
            workflow_name    text        NOT NULL,
            current_state    text        NOT NULL,
            failure_category text        NOT NULL,
            failure_reason   text        NOT NULL,
            failed_step_name text,
            failed_step_index int,
            step_diagnostics jsonb       NOT NULL DEFAULT '[]'::jsonb,
            policy_evaluations jsonb     NOT NULL DEFAULT '[]'::jsonb,
            context_used     jsonb       NOT NULL DEFAULT '[]'::jsonb,
            is_retryable     boolean     NOT NULL,
            suggested_remediation text,
            related_exceptions jsonb     NOT NULL DEFAULT '[]'::jsonb,
            failed_at_utc    timestamptz NOT NULL,
            inspected_at_utc timestamptz NOT NULL DEFAULT now()
        );
        """;
}

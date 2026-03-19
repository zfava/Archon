using Npgsql;
using Testcontainers.PostgreSql;
using ArchonAI.Persistence;
using ArchonAI.Persistence.Stores;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArchonAI.Enterprise.Tests.Infrastructure;

/// <summary>
/// xUnit shared fixture that starts a real PostgreSQL container via Testcontainers,
/// creates the archonai schema and governance tables, and provides factory methods
/// to create fresh <see cref="PostgresGovernanceStore"/> instances.
///
/// Creating a new store instance simulates a service restart — proving that data
/// persisted by one instance is visible to a subsequent instance.
/// </summary>
public sealed class PostgresGovernanceFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("archonai_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Build a raw connection string without the SSL hardening (container is local/plaintext).
        ConnectionString = _container.GetConnectionString();

        // Create schema and governance tables directly (avoids pgvector dependency from migration 001).
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(GovernanceDdl, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Creates a new <see cref="PostgresGovernanceStore"/> instance.
    /// Each call simulates a fresh service instantiation against the same database,
    /// proving that data survives across store lifetimes.
    /// </summary>
    public PostgresGovernanceStore CreateStore()
    {
        var options = Options.Create(new PersistenceOptions
        {
            ConnectionString = ConnectionString,
            Schema = "archonai",
        });

        // Override the hardened connection string to skip SSL for the local test container.
        // PostgresGovernanceStore reads ConnectionStringHardened, which calls Harden().
        // We set the env var so Harden() disables SSL for local test containers.
        Environment.SetEnvironmentVariable("ARCHONAI_POSTGRES_SSL_DISABLE", "true");

        return new PostgresGovernanceStore(options, NullLogger<PostgresGovernanceStore>.Instance);
    }

    /// <summary>
    /// Truncates all governance tables between tests for isolation.
    /// </summary>
    public async Task CleanTablesAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "TRUNCATE archonai.approval_audit_entries, archonai.approval_gates, archonai.approval_policies CASCADE;",
            conn);
        await cmd.ExecuteNonQueryAsync();
    }

    // The governance DDL extracted from migrations 001 (schema only) + 004.
    private const string GovernanceDdl = """
        CREATE SCHEMA IF NOT EXISTS archonai;

        CREATE TABLE IF NOT EXISTS archonai.approval_gates (
            id                uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
            action_type       text        NOT NULL,
            resource_id       text        NOT NULL,
            tenant_id         text        NOT NULL,
            requested_by      text        NOT NULL,
            justification     text        NOT NULL,
            status            int         NOT NULL DEFAULT 0,
            reviewed_by       text,
            review_notes      text,
            requested_at_utc  timestamptz NOT NULL DEFAULT now(),
            reviewed_at_utc   timestamptz,
            action_payload    text,
            execution_status  int         NOT NULL DEFAULT 0,
            execution_error   text,
            executed_at_utc   timestamptz
        );

        CREATE INDEX IF NOT EXISTS idx_approval_gates_tenant_id
            ON archonai.approval_gates (tenant_id);
        CREATE INDEX IF NOT EXISTS idx_approval_gates_status
            ON archonai.approval_gates (status);
        CREATE INDEX IF NOT EXISTS idx_approval_gates_action_type
            ON archonai.approval_gates (action_type);

        CREATE TABLE IF NOT EXISTS archonai.approval_policies (
            id                          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
            action_type                 text        NOT NULL,
            description                 text        NOT NULL,
            required_approver_role      text        NOT NULL,
            require_separation_of_duties boolean    NOT NULL DEFAULT false,
            is_enabled                  boolean     NOT NULL DEFAULT true,
            created_at_utc              timestamptz NOT NULL DEFAULT now()
        );

        CREATE TABLE IF NOT EXISTS archonai.approval_audit_entries (
            id                uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
            approval_gate_id  uuid        NOT NULL REFERENCES archonai.approval_gates(id) ON DELETE CASCADE,
            action_type       text        NOT NULL,
            tenant_id         text        NOT NULL,
            requested_by      text        NOT NULL,
            reviewed_by       text,
            outcome           int         NOT NULL,
            occurred_at_utc   timestamptz NOT NULL DEFAULT now()
        );

        CREATE INDEX IF NOT EXISTS idx_approval_audit_gate_id
            ON archonai.approval_audit_entries (approval_gate_id);
        CREATE INDEX IF NOT EXISTS idx_approval_audit_tenant_id
            ON archonai.approval_audit_entries (tenant_id);
        """;
}

/// <summary>
/// xUnit collection definition that shares the PostgreSQL container across all tests in the collection.
/// </summary>
[CollectionDefinition("PostgresGovernance")]
public class PostgresGovernanceCollection : ICollectionFixture<PostgresGovernanceFixture>
{
}

using Npgsql;
using Testcontainers.PostgreSql;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Persistence;
using ArchonAI.Persistence.Stores;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArchonAI.Enterprise.Tests.Infrastructure;

/// <summary>
/// xUnit shared fixture that starts a real PostgreSQL container via Testcontainers,
/// creates the archonai schema with RBAC and TrustTier tables, and provides factory
/// methods to create fresh store instances.
///
/// Creating a new store instance simulates a service restart — proving that data
/// persisted by one instance is visible to a subsequent instance.
/// </summary>
public sealed class PostgresRbacTrustTierFixture : IAsyncLifetime
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
        ConnectionString = _container.GetConnectionString();

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(Ddl, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Creates a new <see cref="PostgresRbacStore"/> instance.
    /// Each call simulates a fresh service instantiation against the same database.
    /// </summary>
    public PostgresRbacStore CreateRbacStore()
    {
        var options = Options.Create(new PersistenceOptions
        {
            ConnectionString = ConnectionString,
            Schema = "archonai",
        });

        Environment.SetEnvironmentVariable("ARCHONAI_POSTGRES_SSL_DISABLE", "true");
        return new PostgresRbacStore(options, NoOpEventBus.Instance, NullLogger<PostgresRbacStore>.Instance);
    }

    /// <summary>
    /// Creates a new <see cref="PostgresTrustTierStore"/> instance.
    /// Each call simulates a fresh service instantiation against the same database.
    /// </summary>
    public PostgresTrustTierStore CreateTrustTierStore()
    {
        var options = Options.Create(new PersistenceOptions
        {
            ConnectionString = ConnectionString,
            Schema = "archonai",
        });

        Environment.SetEnvironmentVariable("ARCHONAI_POSTGRES_SSL_DISABLE", "true");
        return new PostgresTrustTierStore(options, NullLogger<PostgresTrustTierStore>.Instance);
    }

    /// <summary>
    /// Truncates all RBAC and TrustTier tables between tests for isolation.
    /// </summary>
    public async Task CleanTablesAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "TRUNCATE archonai.rbac_assignments, archonai.rbac_policies, archonai.rbac_roles, archonai.trust_tier_policies CASCADE;",
            conn);
        await cmd.ExecuteNonQueryAsync();
    }

    // Schema + RBAC tables (migration 003) + TrustTier tables (migration 005).
    private const string Ddl = """
        CREATE SCHEMA IF NOT EXISTS archonai;

        CREATE TABLE IF NOT EXISTS archonai.rbac_roles (
            id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
            name           text        NOT NULL,
            description    text        NOT NULL,
            permissions    jsonb       NOT NULL DEFAULT '[]'::jsonb,
            is_system      boolean     NOT NULL DEFAULT false,
            created_at_utc timestamptz NOT NULL DEFAULT now()
        );

        CREATE TABLE IF NOT EXISTS archonai.rbac_assignments (
            id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
            subject_id      text        NOT NULL,
            subject_type    text        NOT NULL,
            role_id         uuid        NOT NULL REFERENCES archonai.rbac_roles(id) ON DELETE CASCADE,
            assigned_by     text        NOT NULL,
            assigned_at_utc timestamptz NOT NULL DEFAULT now()
        );

        CREATE INDEX IF NOT EXISTS idx_rbac_assignments_subject_id
            ON archonai.rbac_assignments (subject_id);
        CREATE INDEX IF NOT EXISTS idx_rbac_assignments_role_id
            ON archonai.rbac_assignments (role_id);

        CREATE TABLE IF NOT EXISTS archonai.rbac_policies (
            id                   uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
            name                 text        NOT NULL,
            description          text        NOT NULL,
            required_permissions jsonb       NOT NULL DEFAULT '[]'::jsonb,
            resource             text        NOT NULL,
            effect               text        NOT NULL,
            conditions           jsonb       NOT NULL DEFAULT '{}'::jsonb,
            is_enabled           boolean     NOT NULL DEFAULT true,
            created_at_utc       timestamptz NOT NULL DEFAULT now()
        );

        CREATE TABLE IF NOT EXISTS archonai.trust_tier_policies (
            id                   uuid             PRIMARY KEY DEFAULT gen_random_uuid(),
            tenant_id            text             NOT NULL,
            action_scope         text             NOT NULL,
            max_tier             int              NOT NULL,
            confidence_threshold double precision,
            value_ceiling        numeric,
            require_reversible   boolean          NOT NULL DEFAULT false,
            description          text,
            is_enabled           boolean          NOT NULL DEFAULT true,
            created_by           text             NOT NULL,
            created_at_utc       timestamptz      NOT NULL DEFAULT now(),
            updated_at_utc       timestamptz      NOT NULL DEFAULT now()
        );

        CREATE INDEX IF NOT EXISTS idx_trust_tier_policies_tenant_id
            ON archonai.trust_tier_policies (tenant_id);
        CREATE INDEX IF NOT EXISTS idx_trust_tier_policies_action_scope
            ON archonai.trust_tier_policies (action_scope);
        CREATE INDEX IF NOT EXISTS idx_trust_tier_policies_tenant_scope
            ON archonai.trust_tier_policies (tenant_id, action_scope);
        """;
}

/// <summary>
/// No-op <see cref="IEventBus"/> implementation for integration tests.
/// PostgresRbacStore requires an IEventBus in its constructor; this stub
/// accepts all publishes without side effects.
/// </summary>
internal sealed class NoOpEventBus : IEventBus
{
    public static readonly NoOpEventBus Instance = new();
    private NoOpEventBus() { }

    public Task PublishAsync(SystemEvent systemEvent, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SubscribeAsync(string eventType, Func<SystemEvent, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// xUnit collection definition that shares the PostgreSQL container across all RBAC/TrustTier tests.
/// </summary>
[CollectionDefinition("PostgresRbacTrustTier")]
public class PostgresRbacTrustTierCollection : ICollectionFixture<PostgresRbacTrustTierFixture>
{
}

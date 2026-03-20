using Npgsql;
using Testcontainers.PostgreSql;
using ArchonAI.Core.Interfaces;
using ArchonAI.Persistence;
using ArchonAI.Persistence.Stores;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArchonAI.Enterprise.Tests.Infrastructure;

/// <summary>
/// xUnit shared fixture that starts a real PostgreSQL container via Testcontainers,
/// creates the archonai schema with Agent Registry and Control Plane tables, and
/// provides factory methods to create fresh store instances.
///
/// Creating a new store instance simulates a service restart — proving that data
/// persisted by one instance is visible to a subsequent instance (multi-instance
/// correctness).
/// </summary>
public sealed class PostgresAgentRegistryControlPlaneFixture : IAsyncLifetime
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

    public PostgresAgentRegistryStore CreateAgentRegistryStore()
    {
        var options = Options.Create(new PersistenceOptions
        {
            ConnectionString = ConnectionString,
            Schema = "archonai",
        });

        Environment.SetEnvironmentVariable("ARCHONAI_POSTGRES_SSL_DISABLE", "true");
        return new PostgresAgentRegistryStore(options, NullLogger<PostgresAgentRegistryStore>.Instance);
    }

    public PostgresControlPlaneStore CreateControlPlaneStore()
    {
        var options = Options.Create(new PersistenceOptions
        {
            ConnectionString = ConnectionString,
            Schema = "archonai",
        });

        Environment.SetEnvironmentVariable("ARCHONAI_POSTGRES_SSL_DISABLE", "true");
        return new PostgresControlPlaneStore(options, NullLogger<PostgresControlPlaneStore>.Instance);
    }

    public PostgresAgentCapabilityRegistryStore CreateAgentCapabilityRegistryStore()
    {
        var options = Options.Create(new PersistenceOptions
        {
            ConnectionString = ConnectionString,
            Schema = "archonai",
        });

        Environment.SetEnvironmentVariable("ARCHONAI_POSTGRES_SSL_DISABLE", "true");
        return new PostgresAgentCapabilityRegistryStore(
            options, NoOpEventBus.Instance,
            NullLogger<PostgresAgentCapabilityRegistryStore>.Instance);
    }

    public PostgresControlPlaneAlertStore CreateControlPlaneAlertStore()
    {
        var options = Options.Create(new PersistenceOptions
        {
            ConnectionString = ConnectionString,
            Schema = "archonai",
        });

        Environment.SetEnvironmentVariable("ARCHONAI_POSTGRES_SSL_DISABLE", "true");
        return new PostgresControlPlaneAlertStore(options, NullLogger<PostgresControlPlaneAlertStore>.Instance);
    }

    public async Task CleanTablesAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            TRUNCATE archonai.agent_metrics, archonai.registered_agents,
                     archonai.platform_configurations, archonai.platform_policies,
                     archonai.managed_agents, archonai.managed_workflows,
                     archonai.tenants,
                     archonai.agent_execution_samples, archonai.agent_capability_profiles,
                     archonai.control_plane_agent_events, archonai.control_plane_alerts
            CASCADE;
            UPDATE archonai.control_plane_system_state SET is_paused = false, pause_reason = NULL;
            """,
            conn);
        await cmd.ExecuteNonQueryAsync();
    }

    // Schema + Agent Registry tables (migration 021) + Control Plane tables (migration 022).
    private const string Ddl = """
        CREATE SCHEMA IF NOT EXISTS archonai;

        -- ── Agent Registry (migration 021) ──────────────────────────

        CREATE TABLE IF NOT EXISTS archonai.registered_agents (
            id                 uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
            name               text        NOT NULL,
            description        text        NOT NULL,
            version            text        NOT NULL,
            status             int         NOT NULL,
            capabilities       jsonb       NOT NULL DEFAULT '[]'::jsonb,
            configuration      jsonb       NOT NULL DEFAULT '{}'::jsonb,
            registered_at_utc  timestamptz NOT NULL DEFAULT now(),
            last_heartbeat_utc timestamptz,
            disabled_at_utc    timestamptz
        );

        CREATE INDEX IF NOT EXISTS idx_registered_agents_name
            ON archonai.registered_agents (name);
        CREATE INDEX IF NOT EXISTS idx_registered_agents_status
            ON archonai.registered_agents (status);
        CREATE INDEX IF NOT EXISTS idx_registered_agents_capabilities
            ON archonai.registered_agents USING gin (capabilities);

        CREATE TABLE IF NOT EXISTS archonai.agent_metrics (
            id                    uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
            agent_id              uuid        NOT NULL REFERENCES archonai.registered_agents(id) ON DELETE CASCADE,
            total_executions      bigint      NOT NULL DEFAULT 0,
            successful_executions bigint      NOT NULL DEFAULT 0,
            failed_executions     bigint      NOT NULL DEFAULT 0,
            average_latency_ms    double precision NOT NULL DEFAULT 0,
            p95_latency_ms        double precision NOT NULL DEFAULT 0,
            uptime_percent        double precision NOT NULL DEFAULT 0,
            collected_at_utc      timestamptz NOT NULL DEFAULT now()
        );

        CREATE INDEX IF NOT EXISTS idx_agent_metrics_agent_id
            ON archonai.agent_metrics (agent_id);
        CREATE INDEX IF NOT EXISTS idx_agent_metrics_collected_at
            ON archonai.agent_metrics (collected_at_utc DESC);

        -- ── Control Plane (migration 022) ───────────────────────────

        CREATE TABLE IF NOT EXISTS archonai.tenants (
            id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
            name            text        NOT NULL UNIQUE,
            display_name    text        NOT NULL,
            status          int         NOT NULL,
            tier            int         NOT NULL,
            resource_quota  jsonb       NOT NULL DEFAULT '{}'::jsonb,
            metadata        jsonb       NOT NULL DEFAULT '{}'::jsonb,
            created_at_utc  timestamptz NOT NULL DEFAULT now(),
            activated_at_utc   timestamptz,
            suspended_at_utc   timestamptz
        );

        CREATE INDEX IF NOT EXISTS idx_tenants_status ON archonai.tenants (status);
        CREATE INDEX IF NOT EXISTS idx_tenants_name ON archonai.tenants (name);

        CREATE TABLE IF NOT EXISTS archonai.managed_workflows (
            id                   uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
            tenant_id            text        NOT NULL,
            name                 text        NOT NULL,
            description          text        NOT NULL,
            status               int         NOT NULL,
            strategy             text        NOT NULL,
            step_count           int         NOT NULL DEFAULT 0,
            metadata             jsonb       NOT NULL DEFAULT '{}'::jsonb,
            created_at_utc       timestamptz NOT NULL DEFAULT now(),
            last_executed_at_utc timestamptz,
            execution_count      bigint      NOT NULL DEFAULT 0,
            failure_count        bigint      NOT NULL DEFAULT 0
        );

        CREATE INDEX IF NOT EXISTS idx_managed_workflows_tenant ON archonai.managed_workflows (tenant_id);
        CREATE INDEX IF NOT EXISTS idx_managed_workflows_status ON archonai.managed_workflows (status);

        CREATE TABLE IF NOT EXISTS archonai.managed_agents (
            id                uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
            tenant_id         text        NOT NULL,
            name              text        NOT NULL,
            version           text        NOT NULL,
            status            int         NOT NULL,
            capabilities      jsonb       NOT NULL DEFAULT '[]'::jsonb,
            configuration     jsonb       NOT NULL DEFAULT '{}'::jsonb,
            registered_at_utc timestamptz NOT NULL DEFAULT now(),
            last_active_at_utc timestamptz,
            execution_count   bigint      NOT NULL DEFAULT 0,
            failure_count     bigint      NOT NULL DEFAULT 0
        );

        CREATE INDEX IF NOT EXISTS idx_managed_agents_tenant ON archonai.managed_agents (tenant_id);
        CREATE INDEX IF NOT EXISTS idx_managed_agents_status ON archonai.managed_agents (status);

        CREATE TABLE IF NOT EXISTS archonai.platform_policies (
            id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
            tenant_id       text        NOT NULL,
            name            text        NOT NULL,
            description     text        NOT NULL,
            policy_type     int         NOT NULL,
            target_resource text        NOT NULL,
            rules           jsonb       NOT NULL DEFAULT '{}'::jsonb,
            is_enabled      boolean     NOT NULL DEFAULT true,
            priority        int         NOT NULL DEFAULT 0,
            created_at_utc  timestamptz NOT NULL DEFAULT now(),
            updated_at_utc  timestamptz
        );

        CREATE INDEX IF NOT EXISTS idx_platform_policies_tenant ON archonai.platform_policies (tenant_id);
        CREATE INDEX IF NOT EXISTS idx_platform_policies_type ON archonai.platform_policies (policy_type);

        CREATE TABLE IF NOT EXISTS archonai.platform_configurations (
            id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
            tenant_id       text        NOT NULL,
            scope           text        NOT NULL,
            key             text        NOT NULL,
            value           text        NOT NULL,
            description     text,
            is_secret       boolean     NOT NULL DEFAULT false,
            created_at_utc  timestamptz NOT NULL DEFAULT now(),
            updated_at_utc  timestamptz,
            UNIQUE (tenant_id, scope, key)
        );

        CREATE INDEX IF NOT EXISTS idx_platform_configs_tenant ON archonai.platform_configurations (tenant_id);
        CREATE INDEX IF NOT EXISTS idx_platform_configs_scope ON archonai.platform_configurations (tenant_id, scope);

        -- ── Agent Capability Profiles (migration 023) ───────────────

        CREATE TABLE IF NOT EXISTS archonai.agent_capability_profiles (
            agent_id           uuid        PRIMARY KEY,
            agent_name         text        NOT NULL,
            version            text        NOT NULL,
            capabilities       jsonb       NOT NULL DEFAULT '[]'::jsonb,
            tools              jsonb       NOT NULL DEFAULT '[]'::jsonb,
            permissions        jsonb       NOT NULL DEFAULT '[]'::jsonb,
            supported_task_types jsonb     NOT NULL DEFAULT '[]'::jsonb,
            average_latency_ms double precision NOT NULL DEFAULT 0,
            p95_latency_ms     double precision NOT NULL DEFAULT 0,
            average_cost       numeric     NOT NULL DEFAULT 0,
            executions         bigint      NOT NULL DEFAULT 0,
            success_count      bigint      NOT NULL DEFAULT 0,
            failure_count      bigint      NOT NULL DEFAULT 0,
            success_rate       double precision NOT NULL DEFAULT 0,
            throughput         double precision NOT NULL DEFAULT 0,
            is_suspended       boolean     NOT NULL DEFAULT false,
            suspend_reason     text,
            updated_at_utc     timestamptz NOT NULL DEFAULT now()
        );

        CREATE INDEX IF NOT EXISTS idx_agent_cap_profiles_capabilities
            ON archonai.agent_capability_profiles USING gin (capabilities);
        CREATE INDEX IF NOT EXISTS idx_agent_cap_profiles_task_types
            ON archonai.agent_capability_profiles USING gin (supported_task_types);

        CREATE TABLE IF NOT EXISTS archonai.agent_execution_samples (
            id                 uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
            agent_id           uuid        NOT NULL REFERENCES archonai.agent_capability_profiles(agent_id) ON DELETE CASCADE,
            task_type          text,
            success            boolean     NOT NULL,
            latency_ms         double precision NOT NULL,
            cost               numeric     NOT NULL DEFAULT 0,
            recorded_at_utc    timestamptz NOT NULL DEFAULT now()
        );

        CREATE INDEX IF NOT EXISTS idx_agent_exec_samples_agent_recorded
            ON archonai.agent_execution_samples (agent_id, recorded_at_utc DESC);

        -- ── Control Plane Alerts (migration 024) ────────────────────

        CREATE TABLE IF NOT EXISTS archonai.control_plane_system_state (
            id              int         PRIMARY KEY DEFAULT 1 CHECK (id = 1),
            is_paused       boolean     NOT NULL DEFAULT false,
            pause_reason    text,
            updated_at_utc  timestamptz NOT NULL DEFAULT now()
        );

        INSERT INTO archonai.control_plane_system_state (id, is_paused, pause_reason, updated_at_utc)
        VALUES (1, false, NULL, now())
        ON CONFLICT (id) DO NOTHING;

        CREATE TABLE IF NOT EXISTS archonai.control_plane_alerts (
            alert_id        uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
            severity        text        NOT NULL,
            component       text        NOT NULL,
            message         text        NOT NULL,
            is_acknowledged boolean     NOT NULL DEFAULT false,
            raised_at_utc   timestamptz NOT NULL DEFAULT now()
        );

        CREATE TABLE IF NOT EXISTS archonai.control_plane_agent_events (
            id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
            agent_id        uuid        NOT NULL,
            agent_name      text        NOT NULL,
            event_type      text        NOT NULL,
            description     text        NOT NULL,
            occurred_at_utc timestamptz NOT NULL DEFAULT now()
        );

        CREATE INDEX IF NOT EXISTS idx_cp_agent_events_occurred
            ON archonai.control_plane_agent_events (occurred_at_utc DESC);
        """;
}

[CollectionDefinition("PostgresAgentRegistryControlPlane")]
public class PostgresAgentRegistryControlPlaneCollection
    : ICollectionFixture<PostgresAgentRegistryControlPlaneFixture>
{
}

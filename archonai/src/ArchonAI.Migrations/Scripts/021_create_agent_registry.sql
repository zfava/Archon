-- Migration 021: Create agent registry tables
-- Shared PostgreSQL persistence for the agent registry subsystem.
-- Replaces the in-memory ConcurrentDictionary-based AgentRepository
-- to support multi-instance correctness and durable state.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/021_drop_agent_registry.sql

CREATE TABLE IF NOT EXISTS archonai.registered_agents (
    id                 uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    name               text        NOT NULL,
    description        text        NOT NULL,
    version            text        NOT NULL,
    status             int         NOT NULL,  -- 0=Active, 1=Disabled, 2=Draining, 3=Offline
    capabilities       jsonb       NOT NULL DEFAULT '[]'::jsonb,
    configuration      jsonb       NOT NULL DEFAULT '{}'::jsonb,
    registered_at_utc  timestamptz NOT NULL DEFAULT now(),
    last_heartbeat_utc timestamptz,
    disabled_at_utc    timestamptz
);

-- Look up agents by name (common query path)
CREATE INDEX IF NOT EXISTS idx_registered_agents_name
    ON archonai.registered_agents (name);

-- Filter agents by status
CREATE INDEX IF NOT EXISTS idx_registered_agents_status
    ON archonai.registered_agents (status);

-- GIN index for capability-based queries (jsonb array contains)
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

-- Query metrics by agent
CREATE INDEX IF NOT EXISTS idx_agent_metrics_agent_id
    ON archonai.agent_metrics (agent_id);

-- Recent metrics across all agents
CREATE INDEX IF NOT EXISTS idx_agent_metrics_collected_at
    ON archonai.agent_metrics (collected_at_utc DESC);

-- Migration 021 applied successfully

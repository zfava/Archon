-- Migration 023: Create agent capability profile tables
-- Shared PostgreSQL persistence for the agent capability registry.
-- Replaces the in-memory ConcurrentDictionary-based InMemoryAgentCapabilityRegistry
-- to support multi-instance correctness for agent selection and performance scoring.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/023_drop_agent_capability_profiles.sql

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

-- Query by capability (GIN index for jsonb array contains)
CREATE INDEX IF NOT EXISTS idx_agent_cap_profiles_capabilities
    ON archonai.agent_capability_profiles USING gin (capabilities);

-- Query by supported task type
CREATE INDEX IF NOT EXISTS idx_agent_cap_profiles_task_types
    ON archonai.agent_capability_profiles USING gin (supported_task_types);

-- Filter non-suspended agents
CREATE INDEX IF NOT EXISTS idx_agent_cap_profiles_suspended
    ON archonai.agent_capability_profiles (is_suspended)
    WHERE is_suspended = false;

-- Rolling latency samples for P95 computation
CREATE TABLE IF NOT EXISTS archonai.agent_execution_samples (
    id                 uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    agent_id           uuid        NOT NULL REFERENCES archonai.agent_capability_profiles(agent_id) ON DELETE CASCADE,
    task_type          text,
    success            boolean     NOT NULL,
    latency_ms         double precision NOT NULL,
    cost               numeric     NOT NULL DEFAULT 0,
    recorded_at_utc    timestamptz NOT NULL DEFAULT now()
);

-- Query recent samples by agent for P95 computation
CREATE INDEX IF NOT EXISTS idx_agent_exec_samples_agent_recorded
    ON archonai.agent_execution_samples (agent_id, recorded_at_utc DESC);

-- Migration 023 applied successfully

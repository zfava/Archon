-- Migration 012: Create enterprise_memory_records table
-- Multi-layer memory system: Session (4h TTL), Operational, Organizational,
-- Strategic, Relational, Financial. Session-layer records auto-expire.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/012_drop_enterprise_memory.sql

CREATE TABLE IF NOT EXISTS archonai.enterprise_memory_records (
    id              uuid             PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       uuid             NOT NULL,
    layer           int              NOT NULL,  -- 0=Session..5=Financial
    category        text             NOT NULL,
    subject         text             NOT NULL,
    content         text             NOT NULL,
    metadata        jsonb            NOT NULL DEFAULT '{}'::jsonb,
    linked_entities jsonb            NOT NULL DEFAULT '[]'::jsonb,
    tags            jsonb            NOT NULL DEFAULT '[]'::jsonb,
    importance      double precision NOT NULL,
    created_by      text             NOT NULL,
    created_at_utc  timestamptz      NOT NULL DEFAULT now(),
    expires_at_utc  timestamptz           -- NULL = never expires
);

-- Tenant-scoped memory queries
CREATE INDEX IF NOT EXISTS idx_emr_tenant_id
    ON archonai.enterprise_memory_records (tenant_id);

-- Filter by memory layer (Session, Operational, Strategic, etc.)
CREATE INDEX IF NOT EXISTS idx_emr_layer
    ON archonai.enterprise_memory_records (layer);

-- Filter by memory category
CREATE INDEX IF NOT EXISTS idx_emr_category
    ON archonai.enterprise_memory_records (category);

-- Chronological timeline queries (newest first)
CREATE INDEX IF NOT EXISTS idx_emr_created_at_utc
    ON archonai.enterprise_memory_records (created_at_utc DESC);

-- Expiration sweep: find expired session memory records efficiently
CREATE INDEX IF NOT EXISTS idx_emr_expires_at_utc
    ON archonai.enterprise_memory_records (expires_at_utc)
    WHERE expires_at_utc IS NOT NULL;

-- Migration 012 applied successfully

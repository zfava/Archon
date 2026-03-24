-- Migration 011: Create operational twin tables
-- Digital twin of the organization: entities, dependencies, KPIs, bottlenecks, artifacts.
-- Five tables supporting the full operational topology model.
-- Idempotent: uses IF NOT EXISTS
-- Rollback: see Down/011_drop_operational_twin.sql

-- ── Entities ──────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS archonai.twin_entities (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id      uuid        NOT NULL,
    entity_type    int         NOT NULL,  -- 0=Team..6=Objective
    name           text        NOT NULL,
    description    text,
    status         int         NOT NULL,  -- 0=Active..3=Archived
    properties     jsonb       NOT NULL DEFAULT '{}'::jsonb,
    tags           jsonb       NOT NULL DEFAULT '[]'::jsonb,
    created_by     text        NOT NULL,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_at_utc timestamptz NOT NULL DEFAULT now()
);

-- Tenant-scoped entity queries
CREATE INDEX IF NOT EXISTS idx_twin_entities_tenant_id
    ON archonai.twin_entities (tenant_id);

-- Filter by entity type (Team, Function, System, etc.)
CREATE INDEX IF NOT EXISTS idx_twin_entities_entity_type
    ON archonai.twin_entities (entity_type);

-- ── Dependencies ──────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS archonai.twin_dependencies (
    id               uuid             PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id        uuid             NOT NULL,
    from_entity_id   uuid             NOT NULL REFERENCES archonai.twin_entities(id) ON DELETE CASCADE,
    to_entity_id     uuid             NOT NULL REFERENCES archonai.twin_entities(id) ON DELETE CASCADE,
    type             int              NOT NULL,  -- 0=DependsOn..4=Blocks
    label            text,
    criticality_score double precision,
    created_at_utc   timestamptz      NOT NULL DEFAULT now()
);

-- Tenant-scoped dependency queries
CREATE INDEX IF NOT EXISTS idx_twin_deps_tenant_id
    ON archonai.twin_dependencies (tenant_id);

-- Traverse dependencies from a specific entity
CREATE INDEX IF NOT EXISTS idx_twin_deps_from_entity_id
    ON archonai.twin_dependencies (from_entity_id);

-- Reverse dependency lookup
CREATE INDEX IF NOT EXISTS idx_twin_deps_to_entity_id
    ON archonai.twin_dependencies (to_entity_id);

-- ── KPIs ──────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS archonai.twin_kpis (
    entity_id          uuid             NOT NULL REFERENCES archonai.twin_entities(id) ON DELETE CASCADE,
    metric_name        text             NOT NULL,
    current_value      double precision NOT NULL,
    target_value       double precision,
    threshold_warning  double precision,
    threshold_critical double precision,
    direction          int              NOT NULL,  -- 0=HigherIsBetter, 1=LowerIsBetter
    unit               text             NOT NULL,
    measured_at_utc    timestamptz      NOT NULL DEFAULT now(),
    PRIMARY KEY (entity_id, metric_name)
);

-- Look up all KPIs for a specific entity
CREATE INDEX IF NOT EXISTS idx_twin_kpis_entity_id
    ON archonai.twin_kpis (entity_id);

-- ── Bottlenecks ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS archonai.twin_bottlenecks (
    id                 uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id          uuid        NOT NULL,
    affected_entity_id uuid        NOT NULL REFERENCES archonai.twin_entities(id) ON DELETE CASCADE,
    description        text        NOT NULL,
    severity           int         NOT NULL,  -- 0=Low..3=Critical
    root_cause         text,
    is_resolved        boolean     NOT NULL DEFAULT false,
    detected_at_utc    timestamptz NOT NULL DEFAULT now(),
    resolved_at_utc    timestamptz
);

-- Tenant-scoped bottleneck queries
CREATE INDEX IF NOT EXISTS idx_twin_bottlenecks_tenant_id
    ON archonai.twin_bottlenecks (tenant_id);

-- Find bottlenecks affecting a specific entity
CREATE INDEX IF NOT EXISTS idx_twin_bottlenecks_entity_id
    ON archonai.twin_bottlenecks (affected_entity_id);

-- ── Artifact Links ────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS archonai.twin_artifact_links (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    twin_entity_id  uuid        NOT NULL REFERENCES archonai.twin_entities(id) ON DELETE CASCADE,
    artifact_type   text        NOT NULL,
    artifact_id     text        NOT NULL,
    relationship    text        NOT NULL,
    linked_at_utc   timestamptz NOT NULL DEFAULT now()
);

-- Find all artifact links for a twin entity
CREATE INDEX IF NOT EXISTS idx_twin_artifact_links_entity
    ON archonai.twin_artifact_links (twin_entity_id);

-- Migration 011 applied successfully

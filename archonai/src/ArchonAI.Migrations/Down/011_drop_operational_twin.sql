-- Rollback for Migration 011: Drop all operational twin tables
-- Drop in reverse dependency order (child tables first).
DROP TABLE IF EXISTS archonai.twin_artifact_links CASCADE;
DROP TABLE IF EXISTS archonai.twin_bottlenecks CASCADE;
DROP TABLE IF EXISTS archonai.twin_kpis CASCADE;
DROP TABLE IF EXISTS archonai.twin_dependencies CASCADE;
DROP TABLE IF EXISTS archonai.twin_entities CASCADE;

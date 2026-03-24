-- Rollback for Migration 003: Drop RBAC tables
-- WARNING: Destroys all role definitions, assignments, and access policies.
-- Drop in reverse dependency order.
DROP TABLE IF EXISTS archonai.rbac_policies CASCADE;
DROP TABLE IF EXISTS archonai.rbac_assignments CASCADE;
DROP TABLE IF EXISTS archonai.rbac_roles CASCADE;

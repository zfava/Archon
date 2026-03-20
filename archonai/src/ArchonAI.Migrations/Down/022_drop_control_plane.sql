-- Rollback for Migration 022: Drop control plane tables
-- WARNING: Destroys all tenant, managed workflow, managed agent, policy, and
-- configuration data. Drop in reverse dependency order.
DROP TABLE IF EXISTS archonai.platform_configurations CASCADE;
DROP TABLE IF EXISTS archonai.platform_policies CASCADE;
DROP TABLE IF EXISTS archonai.managed_agents CASCADE;
DROP TABLE IF EXISTS archonai.managed_workflows CASCADE;
DROP TABLE IF EXISTS archonai.tenants CASCADE;

-- Rollback for Migration 025: Drop identity tables
-- WARNING: Destroys all user accounts, organizations, memberships, tokens,
-- MFA credentials, OIDC configurations, and login sessions.
-- Drop in reverse dependency order (tables with FK references first,
-- then independent tables).
DROP TABLE IF EXISTS archonai.oidc_login_sessions CASCADE;
DROP TABLE IF EXISTS archonai.external_identity_links CASCADE;
DROP TABLE IF EXISTS archonai.tenant_auth_configs CASCADE;
DROP TABLE IF EXISTS archonai.mfa_policies CASCADE;
DROP TABLE IF EXISTS archonai.mfa_challenges CASCADE;
DROP TABLE IF EXISTS archonai.mfa_recovery_codes CASCADE;
DROP TABLE IF EXISTS archonai.webauthn_credentials CASCADE;
DROP TABLE IF EXISTS archonai.totp_credentials CASCADE;
DROP TABLE IF EXISTS archonai.invite_tokens CASCADE;
DROP TABLE IF EXISTS archonai.refresh_tokens CASCADE;
DROP TABLE IF EXISTS archonai.memberships CASCADE;
DROP TABLE IF EXISTS archonai.organizations CASCADE;
DROP TABLE IF EXISTS archonai.users CASCADE;

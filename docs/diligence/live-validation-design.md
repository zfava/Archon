# Live Runtime Validation — Design & Evidence

## Executive Summary

This document describes the live validation strategy for proving that ArchonAI's identity and connector subsystems integrate correctly with real external systems. The validation targets the two highest-value paths:

1. **OIDC Identity Federation** — Token validation, JWKS signature verification, and client recognition against a real IdP (Okta, Entra ID, or Auth0)
2. **Salesforce Connector** — OAuth2 authentication, SOQL query execution, and rate-limit monitoring against a real Salesforce org

## What Is Validated

### Identity Path: OIDC

The production OIDC implementation (`OidcTokenExchangeService`) performs:
- Discovery document fetch from `{authority}/.well-known/openid-configuration`
- JWKS-based signature verification of external `id_token`
- Issuer, audience, lifetime, and nonce validation
- Algorithm safety check (rejects `alg: none`)
- JIT user provisioning with atomic rollback

**Live probes validate the external-system contract:**

| Probe | Validates | Risk Mitigated |
|-------|-----------|----------------|
| Discovery fetch | IdP metadata endpoint is reachable and well-formed | Misconfigured authority URL |
| JWKS fetch | Signing keys are present and retrievable | Token signature verification would fail silently |
| Authorize reachability | Login redirect will work for end users | Firewall/routing issues |
| Client credential check | `client_id` and `client_secret` are recognized | Silent authentication failures at callback |

### Connector Path: Salesforce

The production Salesforce connector (`SalesforceConnector`) performs:
- OAuth2 password-grant authentication
- SOQL queries via REST API
- Rate-limit monitoring via `Sforce-Limit-Info` header
- Retry with exponential backoff on 429/503/504/408
- Re-authentication on 401

**Live probes validate the external-system contract:**

| Probe | Validates | Risk Mitigated |
|-------|-----------|----------------|
| OAuth2 auth | Credential set is valid, Connected App accepts password grant | Silent auth failure at startup |
| SOQL query | Authenticated API calls return structured data | Permission or API version mismatch |
| Rate-limit header | Header format matches connector's parser | Rate-limit warnings never fire |

## What Is NOT Validated (and Why)

| Excluded | Reason |
|----------|--------|
| Full OIDC browser flow (redirect → callback → token exchange) | Requires browser automation; covered by the individual probe chain |
| Salesforce webhook reception | Salesforce connector does not use webhooks (see connector-framework.md) |
| Other connectors (HubSpot, Slack, etc.) | Scope limited to highest-value path; same framework patterns apply |
| SAML | Not yet implemented (`NotImplementedSamlHandler` registered) |
| Load/stress testing | Covered by separate load test suite (`tests/load/`) |

## Artifact Inventory

| Artifact | Path | Purpose |
|----------|------|---------|
| Smoke test script | `scripts/live-validation-smoke.sh` | Executable probe suite (bash + curl, no .NET required) |
| Environment template | `scripts/live-validation-env.template` | Config contract for required credentials |
| Runbook | `docs/testing/live-validation-runbook.md` | Step-by-step execution and troubleshooting |
| This document | `docs/diligence/live-validation-design.md` | Diligence-facing design rationale |
| JSON report (per run) | `live-validation-results/live-validation-{ts}.json` | Machine-readable evidence |

## Secret Handling

- All credentials are injected via environment variables — **never** hardcoded or committed
- `.env.live-validation` is in `.gitignore`
- `live-validation-results/` is in `.gitignore`
- CI integration uses GitHub Environments with required reviewers and scoped secrets
- The smoke script performs **read-only** operations against Salesforce (SOQL SELECT, no writes)
- The OIDC probes send an intentionally invalid authorization code to validate client recognition without completing a real login

## Evidence Gap

**Current status:** The test harness is complete but live credentials are not provisioned in this environment.

| Component | Status | Blocking Action |
|-----------|--------|-----------------|
| Smoke test script | COMPLETE | — |
| Config contract | COMPLETE | — |
| Runbook | COMPLETE | — |
| Salesforce sandbox credentials | NOT AVAILABLE | Provision sandbox, create Connected App |
| OIDC IdP app registration | NOT AVAILABLE | Register app in chosen IdP |
| First execution evidence | PENDING | Execute after credentials are provisioned |

**No results were faked.** The harness is ready for immediate execution once credentials are available.

## Reproducing Results

```bash
# 1. Configure credentials
cp scripts/live-validation-env.template .env.live-validation
# Fill in values

# 2. Source and run
set -a && source .env.live-validation && set +a
bash scripts/live-validation-smoke.sh

# 3. Evidence is in live-validation-results/
cat live-validation-results/live-validation-*.json
```

Expected JSON output shape:
```json
{
  "timestamp": "2026-03-20T16:00:00Z",
  "suite": "live-validation-smoke",
  "summary": {"pass": 7, "fail": 0, "skip": 0},
  "probes": [
    {"probe": "sf-oauth2-auth", "result": "pass", "ts": "...", "detail": "..."},
    {"probe": "sf-soql-query", "result": "pass", "ts": "...", "detail": "..."},
    ...
  ]
}
```

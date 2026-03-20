# Live Validation Runbook

## Purpose

Prove that at least one identity path (OIDC) and one connector path (Salesforce) work against real external systems — not mocks. This is the minimum high-value live validation for diligence.

## Scope

| Path | System | Auth Flow | Probes |
|------|--------|-----------|--------|
| Identity | Okta / Entra ID / Auth0 | OIDC Authorization Code + PKCE | Discovery, JWKS, authorize reachability, client validation |
| Connector | Salesforce | OAuth2 password grant | Authentication, SOQL query, rate-limit header |

**Not in scope:** All other connectors, SAML, end-to-end browser flows.

## Architecture Under Test

```
┌─────────────────────────────────────────────────────┐
│  scripts/live-validation-smoke.sh                   │
│  (curl-based — no .NET runtime required)            │
└────────┬──────────────────────────┬─────────────────┘
         │                          │
    ┌────▼─────┐            ┌───────▼────────┐
    │ Salesforce│            │ OIDC IdP       │
    │ OAuth2   │            │ (Okta/Entra/   │
    │ + REST   │            │  Auth0)        │
    └──────────┘            └────────────────┘
```

The smoke script exercises the **same external APIs** that `SalesforceConnector.AuthenticateCoreAsync()` and `OidcTokenExchangeService.ValidateExternalTokenAsync()` hit in production. It validates:

1. **Salesforce**: Token acquisition via OAuth2 password grant → SOQL query via REST API → rate-limit header parsing
2. **OIDC**: Discovery document fetch → JWKS signing key retrieval → authorize endpoint reachability → client credential acceptance

## Prerequisites

### Salesforce

1. A Salesforce org (sandbox recommended: `https://test.salesforce.com`)
2. A Connected App with:
   - OAuth2 enabled
   - `api` and `refresh_token` scopes
   - Username-password flow allowed (Settings → OAuth Policies → IP Relaxation = "Relax IP restrictions")
3. A user with API access and a known security token
4. Collect: `CLIENT_ID`, `CLIENT_SECRET`, `USERNAME`, `PASSWORD`, `SECURITY_TOKEN`

### OIDC Identity Provider

Choose one:

#### Okta
1. Okta developer account (free at developer.okta.com)
2. Create an OIDC Web Application
3. Note the org Authorization Server URL: `https://dev-XXXXX.okta.com/oauth2/default`
4. Collect: `AUTHORITY`, `CLIENT_ID`, `CLIENT_SECRET` (optional for Probe 7)

#### Microsoft Entra ID
1. Azure AD / Entra ID tenant
2. App Registration with `openid`, `profile`, `email` API permissions
3. Authority: `https://login.microsoftonline.com/{tenant-id}/v2.0`
4. Collect: `AUTHORITY`, `CLIENT_ID`, `CLIENT_SECRET`

#### Auth0
1. Auth0 tenant
2. Regular Web Application
3. Authority: `https://{tenant}.auth0.com`
4. Collect: `AUTHORITY`, `CLIENT_ID`, `CLIENT_SECRET`

## Execution Steps

### 1. Configure Credentials

```bash
cp scripts/live-validation-env.template .env.live-validation
# Edit .env.live-validation with real values — NEVER commit this file
```

### 2. Run Smoke Tests

```bash
set -a && source .env.live-validation && set +a
bash scripts/live-validation-smoke.sh
```

### 3. Review Output

The script produces:
- Console output with pass/fail/skip per probe
- JSON report in `live-validation-results/live-validation-{timestamp}.json`

### 4. Expected Evidence (All Probes Pass)

```
[HH:MM:SS] ✓ PASS: sf-oauth2-auth — HTTP 200, token obtained, instance=https://...
[HH:MM:SS] ✓ PASS: sf-soql-query — HTTP 200, totalSize=N records
[HH:MM:SS] ✓ PASS: sf-rate-limit-header — Sforce-Limit-Info: api-usage=X/Y
[HH:MM:SS] ✓ PASS: oidc-discovery — issuer=https://..., jwks_uri present
[HH:MM:SS] ✓ PASS: oidc-jwks — HTTP 200, N signing key(s) found
[HH:MM:SS] ✓ PASS: oidc-authorize-reachable — HTTP 302
[HH:MM:SS] ✓ PASS: oidc-client-validation — Client credentials accepted
```

## Probe Reference

| # | Probe | What It Proves | Maps To Production Code |
|---|-------|----------------|------------------------|
| 1 | `sf-oauth2-auth` | Salesforce OAuth2 password grant succeeds | `SalesforceConnector.AuthenticateCoreAsync()` |
| 2 | `sf-soql-query` | Authenticated SOQL query returns data | `SalesforceConnector.ExecuteQueryAsync()` |
| 3 | `sf-rate-limit-header` | Rate limit header is parseable | `SalesforceConnector.UpdateRateLimitInfo()` |
| 4 | `oidc-discovery` | IdP exposes valid discovery document | `OidcTokenExchangeService.ValidateExternalTokenAsync()` line 172 |
| 5 | `oidc-jwks` | JWKS endpoint returns signing keys | `OidcTokenExchangeService.ValidateExternalTokenAsync()` line 187 |
| 6 | `oidc-authorize-reachable` | Authorize endpoint is reachable | `OidcEndpoints.MapOidcEndpoints()` login flow |
| 7 | `oidc-client-validation` | Client ID/secret recognized by IdP | `OidcEndpoints.MapOidcEndpoints()` callback token exchange |

## CI Integration

The smoke tests are **not** in the default CI pipeline. They require external credentials and are run manually or in a gated staging job.

To add as a gated CI step:

```yaml
# In .github/workflows/ci-cd.yml or a separate workflow
live-validation:
  runs-on: ubuntu-latest
  if: github.event_name == 'workflow_dispatch'
  environment: staging  # GitHub environment with required secrets
  steps:
    - uses: actions/checkout@v4
    - name: Run live validation
      env:
        SALESFORCE_CLIENT_ID: ${{ secrets.SALESFORCE_CLIENT_ID }}
        SALESFORCE_CLIENT_SECRET: ${{ secrets.SALESFORCE_CLIENT_SECRET }}
        SALESFORCE_USERNAME: ${{ secrets.SALESFORCE_USERNAME }}
        SALESFORCE_PASSWORD: ${{ secrets.SALESFORCE_PASSWORD }}
        SALESFORCE_SECURITY_TOKEN: ${{ secrets.SALESFORCE_SECURITY_TOKEN }}
        OIDC_AUTHORITY: ${{ secrets.OIDC_AUTHORITY }}
        OIDC_CLIENT_ID: ${{ secrets.OIDC_CLIENT_ID }}
        OIDC_CLIENT_SECRET: ${{ secrets.OIDC_CLIENT_SECRET }}
      run: bash scripts/live-validation-smoke.sh
    - name: Upload report
      if: always()
      uses: actions/upload-artifact@v4
      with:
        name: live-validation-report
        path: live-validation-results/
```

## Current Execution Gap

**Status: Credentials not present in this environment.**

The test harness, config contract, and runbook are complete and ready for immediate execution. The blocking gap is:

| Resource | Status | Owner Action |
|----------|--------|--------------|
| Salesforce sandbox + Connected App | NOT PROVISIONED | Create sandbox, configure Connected App, collect credentials |
| OIDC IdP application registration | NOT PROVISIONED | Register app in Okta/Entra/Auth0, collect authority + client_id |
| GitHub Secrets (for CI gating) | NOT CONFIGURED | Add secrets to `staging` environment in GitHub repo settings |

**Estimated time to first execution:** 30–60 minutes (Salesforce sandbox provisioning is the long pole).

## Troubleshooting

### Salesforce: "authentication failure" / "invalid_grant"
- Verify security token is appended to password (no separator)
- Check Connected App OAuth policies: IP Relaxation must be set to "Relax IP restrictions"
- Confirm user's profile has "API Enabled" permission
- For sandbox: use `SALESFORCE_LOGIN_URL=https://test.salesforce.com`

### OIDC: Discovery returns 404
- Verify authority URL format:
  - Okta: `https://dev-XXXXX.okta.com/oauth2/default` (include `/oauth2/default`)
  - Entra: `https://login.microsoftonline.com/{tenant-id}/v2.0`
  - Auth0: `https://{tenant}.auth0.com` (no trailing path)

### OIDC: Client validation returns 401
- Client ID or Client Secret is incorrect
- For Entra ID: ensure the app registration has a client secret (not a certificate)
- For Auth0: use the "Client Secret (Post)" token endpoint auth method

### General: Connection timeout
- Verify outbound HTTPS (port 443) is not blocked
- If behind a proxy, set `HTTPS_PROXY` environment variable

# ArchonAI Connector Maintenance Runbook

> Last updated: 2026-03-21

This runbook covers operational maintenance for all six specialized ArchonAI connectors. It documents API versions, OAuth flows, token refresh behavior, circuit breaker tuning, health monitoring, sandbox setup, and a quarterly review checklist.

---

## Table of Contents

1. [Connector Inventory](#1-connector-inventory)
2. [Salesforce API Version Update Procedure](#2-salesforce-api-version-update-procedure)
3. [OAuth Token Refresh Behavior](#3-oauth-token-refresh-behavior)
4. [Circuit Breaker Tuning](#4-circuit-breaker-tuning)
5. [Connector Health Monitoring](#5-connector-health-monitoring)
6. [Sandbox Account Setup](#6-sandbox-account-setup)
7. [Quarterly Review Checklist](#7-quarterly-review-checklist)

---

## 1. Connector Inventory

| Connector | System Name | Current API Version | Version Config Location | OAuth Flow Type |
|---|---|---|---|---|
| **Salesforce** | `salesforce` | `v59.0` | `SalesforceOptions.ApiVersion` | Password grant (`grant_type=password`) |
| **HubSpot** | `hubspot` | CRM v3 (URL-embedded) | `HubSpotOptions.BaseUrl` | Refresh token (`grant_type=refresh_token`) |
| **Slack** | `slack` | Unversioned (Web API) | `SlackOptions.BaseUrl` | Bot token (static `xoxb-` token via `auth.test`) |
| **Microsoft 365** | `microsoft365` | Graph v1.0 | `Microsoft365Options.GraphBaseUrl` | Client credentials (`grant_type=client_credentials`) via Azure AD |
| **QuickBooks** | `quickbooks` | v3 (minor version 65) | `QuickBooksOptions.ApiVersion` + `minorversion=65` query param | Refresh token (`grant_type=refresh_token`) with Basic auth header |
| **Google Workspace** | `google-workspace` | Gmail v1, Docs v1, Sheets v4, Drive v3 | `GoogleWorkspaceOptions.{Gmail,Docs,Sheets,Drive}BaseUrl` | Refresh token (`grant_type=refresh_token`) |

### Common Defaults (all connectors)

| Setting | Default | Config Property |
|---|---|---|
| HTTP timeout | 30 seconds | `HttpTimeoutSeconds` |
| Max retries | 3 | `MaxRetries` |
| Retry base delay | 1000 ms (exponential backoff) | `RetryBaseDelayMs` |
| Required roles | `["Operator", "Admin"]` | `RequiredRoles` |

---

## 2. Salesforce API Version Update Procedure

Salesforce releases a new REST API version each quarter (Spring, Summer, Winter, Spring). The connector currently targets **v59.0** (Winter '24). When a new version is released:

### Step-by-step

1. **Check the Salesforce release notes** for deprecations and breaking changes at the Salesforce Developer documentation site. Pay attention to:
   - Removed or changed SOQL behavior
   - Modified REST response schemas
   - New required headers or auth changes

2. **Update the default in `SalesforceOptions.cs`**:
   ```
   File: archonai/src/ArchonAI.Connectors/Salesforce/SalesforceOptions.cs
   Property: ApiVersion (default: "v59.0")
   ```
   Change the default value to the new version string (e.g., `"v62.0"`).

3. **Update the environment template** if used for live validation:
   ```
   File: scripts/live-validation-env.template
   Variable: SALESFORCE_API_VERSION
   ```

4. **Run the unit test suite** — all Salesforce connector tests use mock HTTP handlers and reference the version from `SalesforceOptions`. Ensure the version string in mock URL expectations is updated if tests hard-code it.

5. **Run live validation** against a Salesforce sandbox:
   ```bash
   bash scripts/validate-salesforce.sh
   ```

6. **Deploy to staging** and verify:
   - OAuth authentication succeeds
   - SOQL queries return expected results
   - Record create/update operations work
   - Rate limit headers (`Sforce-Limit-Info`) are still parsed correctly

7. **Deploy to production** after staging validation passes.

### Rollback

If the new version causes issues, revert `SalesforceOptions.ApiVersion` to the previous version. The API version is purely a URL path segment (`/services/data/{ApiVersion}/...`), so rollback is a single config change with no schema migration.

---

## 3. OAuth Token Refresh Behavior

### Salesforce

- **Flow**: Password grant — sends `username`, `password+security_token`, `client_id`, `client_secret` to `{LoginUrl}/services/oauth2/token`.
- **Token lifetime**: Hardcoded to 2 hours from authentication time (`_lastAuthenticatedAtUtc + 2h`).
- **Proactive refresh**: Tokens are refreshed when less than **5 minutes** remain before expiry (see `EnsureAuthenticatedAsync`).
- **On 401**: The retry loop re-authenticates on `HttpStatusCode.Unauthorized`.
- **Thread safety**: Auth is guarded by `SemaphoreSlim(1,1)`.

### HubSpot

- **Flow**: Refresh token grant — sends `refresh_token`, `client_id`, `client_secret` to `https://api.hubapi.com/oauth/v1/token`.
- **Token lifetime**: Parsed from `expires_in` in response (default: 1800s / 30 minutes).
- **Proactive refresh**: Tokens are refreshed when less than **2 minutes** remain.
- **On 401**: The retry loop re-authenticates on `HttpStatusCode.Unauthorized`.
- **Thread safety**: Auth is guarded by `SemaphoreSlim(1,1)`.

### Slack

- **Flow**: Static bot token — no OAuth token exchange. Uses `BotToken` (`xoxb-...`) directly in `Authorization: Bearer` header.
- **Token lifetime**: Indefinite (bot tokens don't expire unless revoked).
- **Validation**: Calls `auth.test` to verify the token is valid on first use.
- **Thread safety**: No auth lock needed (token is static).

### Microsoft 365

- **Flow**: Client credentials grant — sends `client_id`, `client_secret`, `scope` to Azure AD token endpoint (`https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/token`).
- **Token lifetime**: Parsed from `expires_in` (default: 3600s / 1 hour).
- **Proactive refresh**: Tokens are refreshed when less than **5 minutes** remain.
- **Thread safety**: Auth is guarded by `SemaphoreSlim(1,1)`.

### QuickBooks

- **Flow**: Refresh token grant with Basic auth — sends `refresh_token` with `Authorization: Basic base64(client_id:client_secret)` to `https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer`.
- **Token lifetime**: Parsed from `expires_in` (default: 3600s / 1 hour).
- **Proactive refresh**: Tokens are refreshed when less than **5 minutes** remain.
- **On 401**: The retry loop re-authenticates on `HttpStatusCode.Unauthorized`.
- **Thread safety**: Auth is guarded by `SemaphoreSlim(1,1)`.
- **Note**: QuickBooks refresh tokens themselves expire after 100 days. Ensure the stored refresh token is updated when a new one is issued.

### Google Workspace

- **Flow**: Refresh token grant — sends `refresh_token`, `client_id`, `client_secret` to `https://oauth2.googleapis.com/token`.
- **Token lifetime**: Parsed from `expires_in` (default: 3600s / 1 hour).
- **Proactive refresh**: Tokens are refreshed when less than **5 minutes** remain.
- **Thread safety**: Auth is guarded by `SemaphoreSlim(1,1)`.
- **Note**: Supports service account with domain-wide delegation (`ServiceAccountEmail` + `ImpersonateUser`).

---

## 4. Circuit Breaker Tuning

### Architecture

Circuit breaker functionality is provided by `ConnectorResilienceRegistry`, which creates per-connector resilience pipelines via `ResiliencePipelineFactory`. Currently, only the **Salesforce connector** injects `ConnectorResilienceRegistry` — all other connectors use built-in retry logic only.

### Current Behavior

- **Salesforce**: Wraps all API calls in a circuit breaker pipeline. When the circuit opens, requests immediately fail with `ConnectorCircuitOpenException`.
- **Other connectors**: No circuit breaker. They rely on their own `ExecuteWithRetryAsync` method with exponential backoff.

### Retry Defaults (all connectors)

| Parameter | Default | Effect |
|---|---|---|
| `MaxRetries` | 3 | Maximum number of attempts before throwing |
| `RetryBaseDelayMs` | 1000 | Base delay doubles each attempt: 1s → 2s → 4s |
| Retryable status codes | 429, 503, 504, 408 | Only these trigger retry |

### Adjusting Thresholds

To change retry behavior, update the connector's `*Options` class properties via `appsettings.json` or environment variables:

```json
{
  "Salesforce": {
    "MaxRetries": 5,
    "RetryBaseDelayMs": 500,
    "HttpTimeoutSeconds": 60
  }
}
```

To enable circuit breaker for other connectors, inject `ConnectorResilienceRegistry` into their constructors (follow the Salesforce pattern). The circuit breaker thresholds are configured in `ResiliencePipelineFactory`.

### Rate Limit Awareness

Each connector tracks rate limits via vendor-specific headers:

| Connector | Rate Limit Header | Warning Threshold |
|---|---|---|
| Salesforce | `Sforce-Limit-Info` (`api-usage=X/Y`) | < 10% remaining |
| HubSpot | `X-HubSpot-RateLimit-Daily-Remaining` | < 1000 remaining |
| Slack | `X-RateLimit-Remaining` | < 20 remaining |
| Microsoft 365 | `RateLimit-Remaining` | < 50 remaining |
| QuickBooks | `X-RateLimit-Remaining` | < 50 remaining |
| Google Workspace | `X-RateLimit-Remaining` | < 50 remaining |

---

## 5. Connector Health Monitoring

### Shadow Metrics (`ConnectorShadowMetrics`)

Every connector records per-request metrics via `ConnectorShadowMetrics.RecordRequest()`:

- **TotalRequests**: Total API calls made
- **SuccessfulRequests**: 2xx responses
- **FailedRequests**: Non-2xx responses
- **AverageLatencyMs**: Running average latency (stored as ticks for precision)
- **LastRequestAtUtc**: Timestamp of most recent call

Access metrics programmatically:
```csharp
var counters = shadowMetrics.GetCounters("salesforce");
// counters.TotalRequests, counters.SuccessfulRequests, etc.

var allMetrics = shadowMetrics.GetAll();
// Dictionary<string, ConnectorMetricCounters>
```

### Prometheus / OpenTelemetry Export

All connectors emit metrics through `ArchonAI.Common.Observability.Telemetry` using `System.Diagnostics.Metrics`:

| Metric Name Pattern | Type | Tags |
|---|---|---|
| `{Connector}AuthAttempts` | Counter | `result` (success/failure) |
| `{Connector}QueryOps` | Counter | `object_type` or `service` |
| `{Connector}WriteOps` | Counter | `operation`, `object_type` |
| `{Connector}Errors` | Counter | `status_code` |
| `{Connector}Retries` | Counter | — |
| `{Connector}RateLimitRemaining` | Histogram | — |

These are automatically scraped by any OpenTelemetry-compatible collector. Configure the OTLP exporter endpoint in `appsettings.json`.

### Connector Status Endpoints

Each connector exposes a `GetStatus()` method returning a typed status record:

```csharp
var status = salesforceConnector.GetStatus();
// status.IsConnected, status.TokenExpiresAtUtc, status.RateLimitRemaining, etc.
```

Wire these into a health-check endpoint (e.g., `/health/connectors`) for monitoring dashboards.

### Distributed Tracing

All connector operations create `Activity` spans via `ObsTelemetry.ActivitySource`:
- `Salesforce.Authenticate`, `Salesforce.Query`, `Salesforce.CreateRecord`, etc.
- Tags include `sf.object_type`, `sf.record_id`, etc.

---

## 6. Sandbox Account Setup

### Salesforce

1. Sign up for a free Salesforce Developer Edition at the Salesforce developer signup page.
2. Create a Connected App:
   - Setup → App Manager → New Connected App
   - Enable OAuth: Full access (`full`), API (`api`), Refresh token (`refresh_token`)
   - Note the Consumer Key (Client ID) and Consumer Secret
3. Get your Security Token: Settings → Reset My Security Token
4. Configure environment:
   ```bash
   SALESFORCE_LOGIN_URL=https://test.salesforce.com  # Use test.salesforce.com for sandboxes
   SALESFORCE_CLIENT_ID=<consumer_key>
   SALESFORCE_CLIENT_SECRET=<consumer_secret>
   SALESFORCE_USERNAME=<your_email>
   SALESFORCE_PASSWORD=<your_password>
   SALESFORCE_SECURITY_TOKEN=<security_token>
   ```

### HubSpot

1. Create a free HubSpot developer account at the HubSpot developer portal.
2. Create a test app → Get Client ID, Client Secret
3. Install the app on a test portal to get a refresh token
4. Configure environment with `HUBSPOT_CLIENT_ID`, `HUBSPOT_CLIENT_SECRET`, `HUBSPOT_REFRESH_TOKEN`

### Slack

1. Create a Slack workspace for testing
2. Create an app at the Slack API apps page → Bot Token Scopes: `chat:write`, `channels:read`, `channels:history`
3. Install to workspace → copy Bot User OAuth Token (`xoxb-...`)
4. Configure `SLACK_BOT_TOKEN`, `SLACK_SIGNING_SECRET`

### Microsoft 365

1. Register an app in Azure AD → App registrations
2. Grant API permissions: `Mail.ReadWrite`, `Mail.Send`, `Team.ReadBasic.All`, `ChannelMessage.Send`, `Files.ReadWrite.All`
3. Create a client secret
4. Configure `M365_TENANT_ID`, `M365_CLIENT_ID`, `M365_CLIENT_SECRET`

### QuickBooks

1. Create an Intuit developer account at the Intuit developer portal
2. Create an app → get Client ID, Client Secret
3. Use the OAuth 2.0 Playground to get initial refresh token
4. Configure `QUICKBOOKS_CLIENT_ID`, `QUICKBOOKS_CLIENT_SECRET`, `QUICKBOOKS_REFRESH_TOKEN`, `QUICKBOOKS_COMPANY_ID`

### Google Workspace

1. Create a project in Google Cloud Console
2. Enable Gmail API, Google Docs API, Google Sheets API, Google Drive API
3. Create OAuth 2.0 credentials → configure consent screen
4. Use OAuth Playground to get a refresh token
5. Configure `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `GOOGLE_REFRESH_TOKEN`

---

## 7. Quarterly Review Checklist

Run this checklist at the start of each quarter, aligned with Salesforce seasonal releases.

### API Version & Deprecation Review

- [ ] **Salesforce**: Check if a new API version was released. Review Salesforce release notes for deprecated endpoints, changed SOQL behavior, or removed fields. Update `SalesforceOptions.ApiVersion` if needed.
- [ ] **HubSpot**: Review HubSpot developer changelog for v3 CRM API changes. Check for deprecated properties or endpoints.
- [ ] **Slack**: Review Slack API changelog. Check for deprecated Web API methods (`chat.postMessage`, `conversations.list`, `conversations.history`).
- [ ] **Microsoft 365**: Review Microsoft Graph changelog for v1.0 changes. Check for deprecated endpoints or changed permissions model.
- [ ] **QuickBooks**: Review Intuit developer release notes. Check for minor version bumps (currently `minorversion=65`) and deprecated entities.
- [ ] **Google Workspace**: Review Google API deprecation schedule for Gmail, Docs, Sheets, and Drive APIs.

### OAuth Scope & Credential Review

- [ ] Verify all OAuth scopes are still valid and not deprecated
- [ ] Rotate client secrets for connectors that support it (HubSpot, M365, QuickBooks, Google)
- [ ] Verify refresh tokens have not expired (QuickBooks refresh tokens expire after 100 days)
- [ ] Confirm Salesforce security tokens are still valid (they reset on password change)
- [ ] Review Slack bot token scopes — ensure no newly required scopes are missing

### Live Validation

- [ ] Run `scripts/validate-salesforce.sh` against sandbox
- [ ] Run `scripts/validate-oidc.sh` against test IdP
- [ ] Run `scripts/live-validation-smoke.sh` for full integration sweep
- [ ] Review JSON reports in `live-validation-results/` for regressions

### Circuit Breaker & Resilience

- [ ] Review error rates in Prometheus/Grafana for each connector over the last quarter
- [ ] Check if any connector is frequently tripping the circuit breaker
- [ ] Evaluate if `MaxRetries` or `RetryBaseDelayMs` need adjustment based on observed 429/503 rates
- [ ] Verify rate limit warning thresholds are appropriate for current usage volume

### Test Coverage

- [ ] Ensure all connector test suites still pass (`dotnet test`)
- [ ] Review test mocks for any API response format changes
- [ ] Add test cases for any new API features being adopted

### Documentation

- [ ] Update this runbook with any version or process changes
- [ ] Update connector inventory table if defaults changed
- [ ] Archive previous quarter's live validation reports

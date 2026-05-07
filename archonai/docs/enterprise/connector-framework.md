# Connector Framework

## Overview

ArchonAI's connector framework provides a standardized approach to integrating with third-party SaaS platforms. All connectors share a common operational model covering authentication, retry/backoff, rate limiting, error classification, and health reporting.

## Supported Connectors

| Connector | Interface | Auth Flow | Webhook Support |
|-----------|-----------|-----------|-----------------|
| Salesforce | `ISalesforceConnector` | OAuth2 password flow | No |
| HubSpot | `IHubSpotConnector` | OAuth2 refresh token | No |
| Slack | `ISlackConnector` | Bot token | Yes (HMAC-SHA256) |
| QuickBooks | `IQuickBooksConnector` | OAuth2 + Basic auth | No |
| Google Workspace | `IGoogleWorkspaceConnector` | OAuth2 refresh token | No |
| Microsoft 365 | `IMicrosoft365Connector` | OAuth2 client credentials | No |
| CRM (generic) | `ICrmConnector` | HttpClient-based | No |
| ERP (generic) | `IErpConnector` | HttpClient-based | No |
| Financial (generic) | `IFinancialConnector` | HttpClient-based | No |
| Messaging (generic) | `IMessagingConnector` | HttpClient-based | No |

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                 IIntegrationControlService                    │
│  (Dashboard, Health, SyncHistory, Webhooks, Credentials)     │
└───────────────────────┬──────────────────────────────────────┘
                        │
┌───────────────────────┴──────────────────────────────────────┐
│                ConnectorHealthAdapter                         │
│  (Normalizes provider-specific status → unified health)      │
└───────────────────────┬──────────────────────────────────────┘
                        │
    ┌───────────────┬───┴───────┬──────────────┬───────────────┐
    │ Salesforce    │ HubSpot   │ Slack        │ QuickBooks    │
    │ Connector     │ Connector │ Connector    │ Connector     │
    └───────────────┴───────────┴──────────────┴───────────────┘
```

## Connector Interface Hierarchy

```
IConnector (base)
├── ICrmConnector
├── IFinancialConnector
├── IMessagingConnector
├── IErpConnector
├── ISalesforceConnector
├── IHubSpotConnector
├── ISlackConnector
├── IQuickBooksConnector
├── IGoogleWorkspaceConnector
└── IMicrosoft365Connector
```

All connectors implement `IConnector`:
```csharp
public interface IConnector
{
    string SystemName { get; }
    Task PushResultAsync(ExecutionResult result, CancellationToken ct = default);
}
```

## Standardized Behaviors

### 1. Authentication

All SaaS connectors implement authentication with:
- **Semaphore-based locking** — prevents concurrent auth race conditions
- **Token caching** — tokens reused until 5 minutes before expiry
- **Auto-reauthentication** — transparent token refresh on 401 responses
- **Auth result reporting** — structured success/failure responses

### 2. Retry and Backoff

All connectors use exponential backoff with consistent behavior:
- **Retryable status codes:** 429, 503, 504, 408
- **Backoff formula:** `baseDelayMs * (1 << (attempt - 1))`
- **Configurable max retries** (default: 3)
- **Retry-After header support** — respects provider-specified delay
- **Re-authentication on 401** — automatic credential refresh

### 3. Rate Limiting

Each connector monitors provider-specific rate limit headers:

| Provider | Header | Warning Threshold |
|----------|--------|-------------------|
| Salesforce | `Sforce-Limit-Info` | Configurable (RateLimitBufferPercent) |
| HubSpot | `X-HubSpot-RateLimit-Daily-Remaining` | < 1000 |
| Slack | `X-RateLimit-Remaining` | < 20 |
| QuickBooks | `X-RateLimit-Remaining` | < 50 |
| Google | `X-RateLimit-Remaining` | < 50 |
| Microsoft | `RateLimit-Remaining` | < 50 |

### 4. Error Classification

The `ConnectorHealthAdapter.ClassifyHttpError()` method normalizes HTTP errors:

| HTTP Status | Category |
|-------------|----------|
| 401 | `Authentication` |
| 403 | `Authorization` |
| 404 | `NotFound` |
| 408 | `Timeout` |
| 409 | `Conflict` |
| 429 | `RateLimit` |
| 400-499 | `InvalidRequest` |
| 500-599 | `ServerError` |

### 5. Health Reporting

The `ConnectorHealthAdapter` normalizes per-connector status into `ConnectorHealthReport`:

| Status | Criteria |
|--------|----------|
| `Healthy` | Connected, error rate < 5%, no credential warnings |
| `Degraded` | Connected but error rate < 20% or credential expiry warning |
| `Unhealthy` | Previously authenticated but now disconnected |
| `NotConfigured` | Never successfully authenticated |

### 6. Observability

All connectors emit:
- **Audit events** via `IEventBus` for all operations
- **Distributed traces** via `System.Diagnostics.ActivitySource`
- **Telemetry counters** for auth attempts, queries, writes, errors, retries

## Configuration

Each connector is configured via the Options pattern:

```json
{
  "Connectors": {
    "Salesforce": {
      "LoginUrl": "https://login.salesforce.com",
      "ClientId": "...",
      "ClientSecret": "...",
      "Username": "...",
      "Password": "...",
      "SecurityToken": "...",
      "MaxRetries": 3,
      "RetryBaseDelayMs": 1000,
      "HttpTimeoutSeconds": 30
    }
  }
}
```

## Integration Control Service

The `IIntegrationControlService` provides the operational surface:

```csharp
public interface IIntegrationControlService
{
    Task<IntegrationDashboard> GetDashboardAsync(CancellationToken ct);
    Task<ConnectorHealthReport> GetConnectorHealthAsync(string connectorName, CancellationToken ct);
    Task<IReadOnlyList<SyncEvent>> GetRecentSyncsAsync(string? connectorName, int limit, CancellationToken ct);
    Task<IReadOnlyList<SyncEvent>> GetFailedSyncsAsync(string? connectorName, int limit, CancellationToken ct);
    Task<IReadOnlyList<WebhookEvent>> GetWebhookEventsAsync(string? connectorName, int limit, CancellationToken ct);
    Task<IReadOnlyList<CredentialStatus>> GetCredentialStatusesAsync(CancellationToken ct);
    void RecordSyncEvent(SyncEvent syncEvent);
    void RecordWebhookEvent(WebhookEvent webhookEvent);
}
```

## Adding a New Connector

1. Define the interface in `ArchonAI.Core/Interfaces/` extending `IConnector`
2. Create the implementation in `ArchonAI.Connectors/{Provider}/`
3. Add Options class with `SectionName = "Connectors:{Provider}"`
4. Register in `ArchonAI.Connectors/DependencyInjection.cs`
5. Add health reporting to `ConnectorHealthAdapter` (adapter switch case)
6. Add tests in `ArchonAI.Connectors.Tests/{Provider}/`

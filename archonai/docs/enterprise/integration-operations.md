# Integration Operations

## Overview

This document covers the operational aspects of managing ArchonAI's third-party integrations in production: monitoring health, diagnosing failures, managing credentials, and understanding sync behavior.

## Integration Dashboard

The `IIntegrationControlService.GetDashboardAsync()` returns a unified view of all integrations:

```
IntegrationDashboard
├── ConnectorHealth[]     – per-connector health reports
├── RecentSyncs[]         – last 20 sync operations
├── FailedSyncs[]         – last 20 failed syncs
├── CredentialStatuses[]  – per-connector credential state
├── TotalConnectors
├── HealthyConnectors
├── DegradedConnectors
└── UnhealthyConnectors
```

## Health Monitoring

### Health Status Definitions

| Status | Meaning | Action Required |
|--------|---------|-----------------|
| **Healthy** | Connected, low error rate, credentials valid | None |
| **Degraded** | Connected but experiencing issues or credential expiry approaching | Monitor; plan credential rotation |
| **Unhealthy** | Connection lost or authentication failing | Immediate investigation |
| **NotConfigured** | Never authenticated; credentials not set | Configure credentials |
| **Unknown** | Connector doesn't support health reporting | N/A (generic connectors) |

### Credential Expiry Warnings

Connectors report `CredentialExpiryWarning = true` when tokens expire within 60 minutes. This gives operators time to rotate credentials before service interruption.

## Sync Event Tracking

Every connector operation can be recorded as a `SyncEvent`:

```
SyncEvent
├── ConnectorName      – which connector (e.g., "salesforce")
├── OperationType      – what operation (e.g., "QueryAccounts")
├── Direction          – Inbound, Outbound, or Bidirectional
├── Status             – Started, Completed, Failed, PartialSuccess, Retrying
├── RecordsProcessed   – count of records handled
├── RecordsFailed      – count of records that failed
├── ErrorMessage       – human-readable error (on failure)
├── ErrorCategory      – classified error type (Authentication, RateLimit, etc.)
├── CursorState        – pagination/sync cursor for resumability
├── Duration           – wall-clock time
├── StartedAtUtc
└── CompletedAtUtc
```

### Querying Sync History

```csharp
// All recent syncs
var recent = await controlService.GetRecentSyncsAsync(limit: 50);

// Failed syncs for a specific connector
var failed = await controlService.GetFailedSyncsAsync("salesforce", limit: 20);
```

## Webhook Event Visibility

For connectors that support webhooks (currently Slack), events are recorded:

```
WebhookEvent
├── ConnectorName    – which connector received the event
├── EventType        – e.g., "message.posted"
├── SignatureValid   – was the HMAC signature verified?
├── Processed        – was the event successfully processed?
├── ErrorMessage     – processing error if any
└── ReceivedAtUtc
```

Events with invalid signatures are logged as warnings.

## Failure Diagnosis

### Error Classification

When a connector operation fails, the `ConnectorErrorCategory` helps operators quickly understand the root cause:

| Category | Typical Cause | Remediation |
|----------|---------------|-------------|
| `Authentication` | Invalid/expired credentials | Rotate credentials |
| `Authorization` | Insufficient permissions | Check API scopes/permissions |
| `RateLimit` | Too many API calls | Back off; review usage patterns |
| `Timeout` | Provider slow/unresponsive | Retry; check provider status |
| `NetworkFailure` | DNS/TLS/connectivity issues | Check network; retry |
| `InvalidRequest` | Bad payload or parameters | Fix request data |
| `NotFound` | Resource doesn't exist | Verify resource IDs |
| `Conflict` | Concurrent modification | Retry with latest version |
| `ServerError` | Provider-side failure | Retry; check provider status |
| `CredentialExpired` | Token past expiry | Trigger re-authentication |

### Retry Behavior

All connectors retry on transient errors (429, 503, 504, 408) with exponential backoff. The retry sequence:
1. First retry: `baseDelayMs` (default 1000ms)
2. Second retry: `baseDelayMs * 2` (2000ms)
3. Third retry: `baseDelayMs * 4` (4000ms)

If the provider returns `Retry-After`, the connector respects that value (using whichever is larger).

## Rate Limit Management

Each connector tracks remaining API quota. When limits approach warning thresholds, log warnings are emitted. The `ConnectorHealthReport.RateLimitRemaining` field exposes current quota to the dashboard.

## Credential Rotation

### Current Model
Credentials are configured via `appsettings.json` or environment variables. Rotation requires updating the configuration and restarting the affected connector (or the entire process).

### Rotation Checklist
1. Generate new credentials in the third-party platform
2. Update configuration (env vars or appsettings)
3. Restart the service or trigger re-authentication
4. Verify via `ValidateCredentialsAsync()` or the dashboard

## Bounded Event History

Sync events and webhook events are kept in a bounded in-memory buffer (max 1000 events). Older events are evicted when the buffer is full. For production deployments, consider persisting events to a database for long-term analysis.

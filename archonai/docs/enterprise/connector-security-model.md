# Connector Security Model

## Overview

This document describes the security controls, credential handling, and isolation boundaries for ArchonAI's connector framework.

## Credential Management

### Storage

Credentials are provided via the standard .NET configuration system:
- `appsettings.json` (development only)
- Environment variables (production recommended)
- Azure Key Vault, AWS Secrets Manager, or HashiCorp Vault via configuration providers

**Important:** Never store production credentials in `appsettings.json`. Use environment variables or secret management services.

### Credential Types by Connector

| Connector | Credential Type | Sensitivity |
|-----------|----------------|-------------|
| Salesforce | Client ID/Secret + Username/Password + Security Token | High |
| HubSpot | Client ID/Secret + Refresh Token | High |
| Slack | Bot Token + Signing Secret + App Token | High |
| QuickBooks | Client ID/Secret + Refresh Token | High |
| Google Workspace | Client ID/Secret + Refresh Token | High |
| Microsoft 365 | Tenant ID + Client ID/Secret | High |

### Token Lifecycle

1. **Initial auth** — connector authenticates using configured credentials
2. **Token caching** — access tokens held in memory for duration of validity
3. **Expiry buffer** — tokens refreshed 5 minutes before expiry
4. **Auto-refresh** — transparent re-authentication on 401 responses
5. **Concurrent protection** — `SemaphoreSlim` prevents auth race conditions

### Credential Validation

Use `ConnectorHealthAdapter.ValidateCredentialsAsync()` to verify credentials:
- Attempts authentication against the provider
- Returns structured `CredentialStatus` with expiry and validation errors
- Does not store or cache the result

## Authentication Flows

### OAuth2 Password Flow (Salesforce)
```
Client → POST /services/oauth2/token (client_id, client_secret, username, password+security_token)
Client ← {access_token, instance_url}
```

### OAuth2 Refresh Token Flow (HubSpot, QuickBooks, Google)
```
Client → POST /oauth/token (grant_type=refresh_token, client_id, client_secret, refresh_token)
Client ← {access_token, expires_in}
```

### OAuth2 Client Credentials (Microsoft 365)
```
Client → POST /oauth2/v2.0/token (grant_type=client_credentials, client_id, client_secret, scope)
Client ← {access_token, expires_in}
```

### Token Authentication (Slack)
```
Client → POST /auth.test (Authorization: Bearer bot_token)
Client ← {ok, team_id, bot_id}
```

## Webhook Security

### Slack Webhook Signature Verification

Slack webhooks use HMAC-SHA256 signature verification:

1. Construct the signature base string: `v0:{timestamp}:{request_body}`
2. Compute HMAC-SHA256 using the Signing Secret
3. Compare computed signature with the `X-Slack-Signature` header
4. Verify timestamp is within 5 minutes (replay attack prevention)

```csharp
// Signature verification (simplified)
var baseString = $"v0:{timestamp}:{requestBody}";
var expectedSignature = "v0=" + HMAC_SHA256(signingSecret, baseString);
var isValid = CryptographicOperations.FixedTimeEquals(expected, received);
```

### Webhook Security Checklist
- Always verify signatures before processing events
- Reject events with timestamps > 5 minutes old (replay prevention)
- Log invalid signature attempts for security monitoring
- Use constant-time comparison for signature validation

## Tenant Isolation

### Current Model

Connectors are registered as **singletons** in the DI container, meaning all tenants share the same connector instances and credentials. This is appropriate for platform-level integrations (e.g., the platform's own Salesforce org).

### Multi-Tenant Isolation Requirements

For tenant-specific integrations (each tenant has their own Salesforce org), implement:
1. **Per-tenant credential storage** — store credentials in the control plane per tenant
2. **Tenant-scoped connector factory** — create connector instances per tenant
3. **Credential isolation** — ensure one tenant cannot access another's credentials
4. **Rate limit isolation** — track rate limits per tenant to prevent noisy-neighbor issues

### API Authorization

Each connector's Options includes `RequiredRoles` (default: `["Operator", "Admin"]`). API endpoints exposing connector operations should enforce these roles.

## Error Handling Security

### Information Disclosure Prevention
- Error messages logged internally include full details
- Error responses to API consumers should be sanitized
- Never expose raw provider error responses containing internal URLs or tokens

### Error Classification
The `ConnectorErrorCategory` enum classifies errors without exposing internal details:
```
Authentication → "Credential validation failed"
Authorization → "Insufficient permissions"
RateLimit → "Rate limit exceeded, retry later"
ServerError → "Provider service error, retry later"
```

## Audit Trail

All connector operations emit `SystemEvent` records via `IEventBus`:
- `salesforce.record.created` — record creation
- `salesforce.record.updated` — record modification
- `salesforce.result.pushed` — execution result dispatch
- Similar patterns for all connectors

Events include: operation type, object type, record ID, and timestamp.

## Security Recommendations

### Immediate
- Store all credentials in environment variables or secret management services
- Enable webhook signature verification for all webhook-capable connectors
- Monitor credential expiry warnings in the integration dashboard

### Medium-term
- Implement credential rotation automation
- Add per-tenant credential isolation for multi-tenant deployments
- Add rate limit alerting (not just logging)
- Implement credential encryption at rest

### Long-term
- Integrate with enterprise SSO for connector authentication
- Implement Just-In-Time credential provisioning
- Add connector-level audit log persistence
- Implement connector-level circuit breakers

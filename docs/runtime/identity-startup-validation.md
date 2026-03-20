# Identity Startup Validation

## Overview

ArchonAI validates identity/security/persistence configuration at startup to prevent unsafe deployments from accepting traffic.

## Validation Rules

### Production/Staging Environments

When `ASPNETCORE_ENVIRONMENT` is `Production` or `Staging`:

1. **Durable persistence required** — `ArchonAIPersistence:ConnectionString` must be set. If absent, the application **logs a CRITICAL error and shuts down immediately**.
2. **TOTP encryption key required** — Either `ARCHONAI_TOTP_ENCRYPTION_KEY` or `ARCHONAI_JWT_SIGNING_KEY` must be set. If neither is available when TOTP operations are attempted, the operation throws `InvalidOperationException`.

### Development/Test Environments

When `ASPNETCORE_ENVIRONMENT` is `Development` or any non-production value:

1. **In-memory persistence allowed** — Identity stores fall back to in-memory implementations with a `LogWarning` at startup.
2. **TOTP encryption key still required** — No hardcoded fallback exists. Set the key in your `.env` file or test configuration.

## Health Check: `identity_persistence`

The `IdentityPersistenceHealthCheck` is registered on the `/healthz/ready` endpoint:

| Scenario | Result |
|---|---|
| PostgreSQL persistence configured | Healthy |
| In-memory persistence in production | **Unhealthy** — readiness probe fails |
| In-memory persistence in development | Degraded — warns but allows traffic |

This ensures Kubernetes will not route traffic to pods with unsafe identity configuration.

## Startup Sequence

```
1. Build service container
2. Evaluate environment + persistence config
3. If production + no ConnectionString → log CRITICAL, return (app exits)
4. If development + no ConnectionString → log WARNING, continue
5. Configure IdentityPersistenceHealthCheck state
6. Run database migrations (if ConnectionString set)
7. If migration fails → log CRITICAL, return (app exits)
8. Start accepting traffic
```

## Deployment Checklist

- [ ] `ArchonAIPersistence:ConnectionString` is set and points to a reachable PostgreSQL instance
- [ ] `ARCHONAI_JWT_SIGNING_KEY` is set (minimum 32 characters)
- [ ] `ARCHONAI_TOTP_ENCRYPTION_KEY` is set (recommended) or JWT key is available as fallback
- [ ] Migration 025 (identity tables) has been applied
- [ ] `/healthz/ready` returns Healthy after startup

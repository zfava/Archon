# Deployment Validation Checklist

Use this checklist to verify a new deployment is healthy before routing production traffic.

## Pre-Deploy

- [ ] All changes committed and CI green
- [ ] Database migrations applied (if any)
- [ ] Configuration changes reviewed (appsettings, env vars)
- [ ] JWT signing key configured (`ARCHONAI_JWT_SIGNING_KEY`, >=32 chars)
- [ ] Model provider API keys configured (OpenAI, Azure, Anthropic as needed)
- [ ] Connector credentials configured (Salesforce, HubSpot, etc.)

## Startup Verification

### Gateway
```bash
# 1. Health check passes
curl -f http://gateway:5100/healthz/live
# Expected: 200 OK

# 2. Readiness probe passes
curl -f http://gateway:5100/healthz/ready
# Expected: 200 OK

# 3. Legacy health endpoint
curl -s http://gateway:5100/health | jq '.status'
# Expected: "ok"
```

### API Server
```bash
# 1. Liveness probe
curl -f http://api:5000/healthz/live
# Expected: 200 OK

# 2. Readiness probe (waits for agent/tool registration)
curl -f http://api:5000/healthz/ready
# Expected: 200 OK (may take 10-30s after startup)

# 3. Full health report
curl -s http://api:5000/healthz | jq
# Expected: All checks "Healthy"

# 4. Auth endpoint responds
curl -s -X POST http://api:5000/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"test@example.com","password":"testpassword"}'
# Expected: 200 with tokens, or 401 (either is fine — endpoint is live)
```

### Workers
```bash
# Workers register via cluster heartbeat — verify they appear
curl -s -H "Authorization: Bearer $TOKEN" \
  http://api:5000/api/v1/cluster/nodes | jq '.[].role'
# Expected: ["runtime", "scheduler", "agents"]
```

## Functional Smoke Tests

### Authentication
- [ ] Login with valid credentials returns JWT tokens
- [ ] Protected endpoints reject unauthenticated requests (401)
- [ ] Admin-only endpoints reject non-admin users (403)
- [ ] Token refresh works within expiry window

### Core API
- [ ] `GET /api/v1/health` returns version info (requires auth)
- [ ] `GET /api/v1/registry/agents` returns registered agents
- [ ] `GET /api/v1/registry/capabilities` returns capabilities

### Connectors
- [ ] Each configured connector's `/status` endpoint responds
- [ ] Connector authentication succeeds (check `.isAuthenticated`)
- [ ] Rate limit remaining is reasonable (not near zero)

### Real-Time
- [ ] SignalR hub accepts connections at `/hubs/control-plane-dashboard`
- [ ] Dashboard broadcast service is emitting updates

## Observability Verification

### Metrics
```bash
# Gateway Prometheus metrics are being scraped
curl -s -H "Authorization: Bearer $ADMIN_TOKEN" \
  http://gateway:5100/metrics | grep archonai_gateway_requests_total
# Expected: counter value > 0 (from health checks)
```

### Logging
- [ ] Structured logs appear in console output
- [ ] Logs include `CorrelationId` property
- [ ] Logs include `Service` property
- [ ] Request logs show method, path, status code, duration

### Health Checks
```bash
# All health checks passing
curl -s http://api:5000/healthz | jq '.checks[] | {name, status}'
# Expected: All "Healthy"
```

## Performance Baseline

After deploy, capture baseline metrics for comparison:

- [ ] `archonai.gateway.request.duration.ms` p50, p95, p99
- [ ] `archonai.task.execution.duration.ms` p50, p95
- [ ] `archonai.model.invocation.duration.ms` p50, p95 by provider
- [ ] Gateway requests/sec at steady state
- [ ] Queue backlog depth at steady state

## Rollback Criteria

Immediately roll back if any of these occur within 15 minutes of deploy:

1. **Health probe failures** — `/healthz/live` or `/healthz/ready` returning non-200
2. **Error rate spike** — `archonai.gateway.requests.failed` > 5% of total
3. **Latency regression** — p95 latency > 2x pre-deploy baseline
4. **Auth failures** — Legitimate users unable to authenticate
5. **Data corruption** — Audit log integrity verification fails

## Rollback Steps

1. Revert to previous container image / deployment
2. Verify health probes pass on rolled-back version
3. Check that no database migration needs reverting
4. Monitor error rates for 10 minutes post-rollback
5. Open incident for root cause analysis

## Post-Deploy Monitoring

Monitor these for 30 minutes after deploy:

- [ ] Error rate stable (not increasing)
- [ ] Latency stable (not degrading)
- [ ] No new error patterns in structured logs
- [ ] Intelligence loop completing cycles
- [ ] Dashboard broadcast service emitting updates
- [ ] Queue backlog not growing unbounded

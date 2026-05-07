# Operations Runbook

## Service Inventory

| Service | Purpose | Default Port | Health Endpoint |
|---------|---------|-------------|-----------------|
| ArchonAI.Gateway | YARP reverse proxy, rate limiting, auth | 5100 | `/healthz/live`, `/healthz/ready` |
| ArchonAI.Api | Core API, all business logic | 5000 | `/healthz/live`, `/healthz/ready`, `/healthz` |
| ArchonAI.Worker.Runtime | Task execution, workflow engine | 5200 | Process health via cluster heartbeat |
| ArchonAI.Worker.Scheduler | Task scheduling, distribution | 5300 | Process health via cluster heartbeat |
| ArchonAI.Worker.Agents | Agent hosting (Finance, Ops, Sales, Marketing, Support) | 5400 | Process health via cluster heartbeat |

## Startup Sequence

1. **ArchonAI.Api** starts first — registers agents, tools, event bus
2. **Workers** start and register with the cluster via heartbeat
3. **ArchonAI.Gateway** starts last — proxies traffic to API

### Startup Readiness Signals

The API reports ready only after:
- Agent registration completes (`StartupComponent.AgentRegistration`)
- Tool registration completes (`StartupComponent.ToolRegistration`)
- Event bus is connected (`StartupComponent.EventBus`)

Check with: `GET /healthz/ready`

## SLO / SLI Suggestions

### Availability SLO: 99.9% (8.76h downtime/year)

**SLI**: `1 - (archonai.gateway.requests.failed / archonai.gateway.requests.total)`

Measured over rolling 30-day windows. Exclude health check and metrics endpoints.

### Latency SLO: p95 < 2s for API requests

**SLI**: `histogram_quantile(0.95, archonai.gateway.request.duration.ms)`

Excludes model invocation endpoints (which depend on LLM provider latency).

### Task Completion SLO: 95% of tasks complete within 60s

**SLI**: `archonai.tasks.executed / (archonai.tasks.executed + archonai.tasks.failed)` with latency threshold `archonai.task.execution.duration.ms < 60000`

### Model Invocation SLO: 99% success rate

**SLI**: `1 - (archonai.model.invocations.failed / archonai.model.invocations.total)` per provider

## Failure Mode Catalog

### 1. Model Provider Outage

**Symptoms**: `archonai.model.invocations.failed` spike, `model_providers` health check degraded

**Impact**: Intelligence loop stalls, agent tasks fail, command console returns errors

**Diagnosis**:
```bash
# Check model provider health
curl -s http://localhost:5000/healthz | jq '.checks[] | select(.name == "model_providers")'

# Check recent failures by provider
# (Prometheus query)
sum(rate(archonai_model_invocations_failed_total[5m])) by (provider)
```

**Mitigation**:
1. Check provider status page (OpenAI, Azure, Anthropic)
2. If single provider: the `CompositeModelProvider` will failover to alternatives
3. If all providers: intelligence loop will continue retrying with backoff
4. No manual intervention needed — system recovers automatically when provider returns

### 2. Task Queue Backlog

**Symptoms**: `archonai.runtime.queue.backlog` > 500, `task_queue` health check degraded

**Impact**: Task execution delayed, real-time dashboard shows stale data

**Diagnosis**:
```bash
curl -s http://localhost:5000/healthz | jq '.checks[] | select(.name == "task_queue")'
```

**Mitigation**:
1. Check if workers are running: cluster status endpoint
2. Scale Worker.Runtime horizontally
3. Check for stuck tasks: `/api/v1/runtime-health/recoveries`
4. If persistent: check event bus (NATS) connectivity

### 3. Connector Authentication Failure

**Symptoms**: `archonai.{connector}.auth.attempts` with no corresponding successful ops, connector `/status` returns unauthenticated

**Impact**: Agent tasks that depend on connector data will fail or return incomplete results

**Diagnosis**:
```bash
# Check specific connector
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5000/api/v1/connectors/salesforce/status
```

**Mitigation**:
1. Verify credentials in configuration or environment variables
2. Check if OAuth token has expired — re-authenticate
3. Check connector provider's status page for outages
4. Verify rate limits haven't been exhausted

### 4. Intelligence Loop Stall

**Symptoms**: `archonai.loop.cycles.completed` flat for > 10 minutes

**Impact**: No new goals generated, no proactive operations

**Diagnosis**:
```bash
# Check intelligence loop cycle metrics
# (Prometheus query)
rate(archonai_loop_cycles_completed_total[5m])
rate(archonai_loop_cycles_failures_total[5m])
```

**Mitigation**:
1. Check API logs for intelligence loop errors
2. Verify `IntelligenceLoopHostedService` is running (background service logs)
3. Check if perception signals are being received
4. Restart the API service if loop is truly stalled

### 5. Gateway Rate Limiting

**Symptoms**: `archonai.gateway.ratelimit.hits` increasing, clients receiving 429 responses

**Impact**: Legitimate requests blocked

**Diagnosis**:
```bash
curl -s -H "Authorization: Bearer $ADMIN_TOKEN" http://localhost:5100/gateway/status | jq
```

**Mitigation**:
1. Check if rate limits are appropriate for current load
2. Identify the source (route category from gateway status)
3. Adjust rate limit policies in Gateway configuration
4. Scale gateway horizontally if legitimate traffic

### 6. Cluster Node Failure

**Symptoms**: Cluster heartbeat stops for a node, tasks not being scheduled

**Diagnosis**:
```bash
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5000/api/v1/cluster/nodes?status=unreachable
```

**Mitigation**:
1. Check node process health
2. Verify network connectivity between nodes
3. The cluster will automatically rebalance workloads
4. Restart failed node — it will re-register via heartbeat

## Incident Response

### Severity Levels

| Level | Criteria | Response Time | Example |
|-------|----------|---------------|---------|
| P1 | Service unavailable, data loss risk | 15 min | Gateway down, all model providers failed |
| P2 | Significant degradation | 1 hour | Single connector failed, queue backlog >2000 |
| P3 | Minor degradation | 4 hours | Elevated latency, single model provider issues |
| P4 | Cosmetic / low impact | 24 hours | Dashboard data stale, non-critical log noise |

### Triage Steps

1. Check health endpoints: `GET /healthz` on Gateway and API
2. Check gateway status: `GET /gateway/status`
3. Check Prometheus metrics for anomalies
4. Review structured logs with correlation ID from the failing request
5. Check cluster node status
6. Check connector status endpoints

### Communication Template

```
INCIDENT: [Brief description]
SEVERITY: P[1-4]
IMPACT: [What users/systems are affected]
STATUS: [Investigating | Identified | Monitoring | Resolved]
TIMELINE:
  [HH:MM UTC] - Issue detected
  [HH:MM UTC] - [Action taken]
ROOT CAUSE: [Once identified]
RESOLUTION: [Once resolved]
```

## Key Configuration

### Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `ARCHONAI_JWT_SIGNING_KEY` | Yes | JWT signing key (>=32 chars) |
| `VITE_API_BASE_URL` | No | Frontend API base URL override |

### Rate Limiting Tiers

| Policy | Limit | Window | Queue |
|--------|-------|--------|-------|
| standard | 120/min | 1 min | 20 |
| admin | 60/min | 1 min | 10 |
| connectors | 200/min | 1 min | 30 |
| health | 300/min | 1 min | 0 |
| metrics | 30/min | 1 min | 0 |

### Background Service Intervals

| Service | Default Interval | Config Key |
|---------|-----------------|------------|
| RuntimeHealthMonitor | Configurable | `RuntimeHealth:MonitorIntervalSeconds` |
| ClusterNodeHeartbeat | 30s | `ClusterNodeRegistration:*` |
| ClusterRebalance | Configurable | `Cluster:RebalanceIntervalSeconds` |
| DashboardBroadcast | Configurable | `ControlPlane:BroadcastIntervalSeconds` |
| IntelligenceLoop | Configurable | `IntelligenceLoop:CycleIntervalSeconds` |

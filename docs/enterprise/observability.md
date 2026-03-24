# Observability

## Overview

ArchonAI uses a layered observability stack: **structured logging** (Serilog), **distributed tracing** (OpenTelemetry), **metrics** (OpenTelemetry + Prometheus), and **health checks** (ASP.NET Core Health Checks). All services emit correlated signals that can be aggregated in Grafana, Datadog, or any OTLP-compatible backend.

## Architecture

```
                    ┌─────────────┐
                    │  Prometheus  │ ◄── /metrics (Gateway)
                    └──────┬──────┘     /metrics (API via Prometheus exporter)
                           │
┌─────────┐  ┌────────────┐│  ┌──────────────────┐
│ Gateway  │──│ API Server ││──│ Worker.Runtime   │
│ (YARP)   │  │            ││  │ Worker.Scheduler │
│          │  │            ││  │ Worker.Agents    │
└─────────┘  └────────────┘│  └──────────────────┘
     │             │       │           │
     ▼             ▼       ▼           ▼
  Serilog       Serilog  OTel      Serilog
  Console       Console  Traces    Console
```

## Structured Logging

### Configuration

All services use Serilog with console sink and structured JSON output. Configuration via `appsettings.json`:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.AspNetCore": "Warning",
        "System.Net.Http": "Warning"
      }
    }
  }
}
```

### Log Enrichment

Every log entry is enriched with:

| Property | Source | Description |
|----------|--------|-------------|
| `Service` | Static | Service name (e.g., `ArchonAI`, `ArchonAI.Gateway`) |
| `CorrelationId` | Header / Generated | Request correlation ID (X-Correlation-Id) |
| `TenantId` | JWT claim | Current tenant scope |
| `UserId` | JWT claim | Authenticated user ID |
| `UserRole` | JWT claim | User's RBAC role |
| `WorkflowId` | Header | Active workflow context (X-Workflow-Id) |
| `AgentId` | Header | Active agent context (X-Agent-Id) |
| `RequestMethod` | HTTP | GET, POST, etc. |
| `RequestPath` | HTTP | API path |

### Key Log Points

- **Request start/end** — via `UseSerilogRequestLogging()`
- **Auth failures** — 401/403 responses logged at Warning level
- **Server errors** — 5xx responses logged with full context
- **Background service lifecycle** — Start/stop/error for all hosted services
- **Model provider retries** — Each retry attempt with delay and correlation
- **Intelligence loop cycles** — Goals generated, tasks executed, duration per cycle

## Distributed Tracing

### ActivitySource

All services share a single `ActivitySource` named `ArchonAI` defined in `ArchonAI.Common.Observability.Telemetry`.

### Instrumentation

| Instrumentation | Service | Description |
|-----------------|---------|-------------|
| ASP.NET Core | Gateway, API | HTTP request spans |
| HttpClient | Gateway, API | Outbound HTTP calls |
| Runtime | API | GC, thread pool, assembly metrics |
| Process | API | CPU, memory, handle counts |
| Custom spans | API | Model invocations, workflow execution |

### Request Correlation

The `RequestCorrelationMiddleware` (API) creates an Activity span for every request with tags:

- `correlation.id` — Links all operations for a single request
- `tenant.id` — Multi-tenant scope
- `user.id` — Authenticated user
- `workflow.id` — Active workflow (when provided via header)
- `agent.id` — Active agent (when provided via header)
- `http.method`, `http.path`, `http.status_code`, `http.duration_ms`

### Model Invocation Spans

`ModelInvocationTelemetry.StartInvocationSpan()` creates child spans for LLM calls:

- `model.provider` — openai, azure, anthropic, local
- `model.name` — Specific model identifier
- `correlation.id` — Parent request correlation
- `agent.id` — Agent making the call

## Metrics

### Prometheus Endpoints

| Service | Endpoint | Auth |
|---------|----------|------|
| Gateway | `/metrics` | AdminOnly |
| API | Via Prometheus exporter | AdminOnly |

### Metric Inventory (200+ counters/histograms)

#### Core Operations
| Metric | Type | Description |
|--------|------|-------------|
| `archonai.tasks.queued` | Counter | Tasks added to execution queue |
| `archonai.tasks.executed` | Counter | Tasks completed |
| `archonai.tasks.failed` | Counter | Tasks failed |
| `archonai.task.execution.duration.ms` | Histogram | Task execution latency |

#### Model Invocations
| Metric | Type | Description |
|--------|------|-------------|
| `archonai.model.invocations.total` | Counter | Total LLM invocations (by provider, model) |
| `archonai.model.invocations.failed` | Counter | Failed invocations |
| `archonai.model.invocations.retried` | Counter | Retried invocations |
| `archonai.model.invocations.timedout` | Counter | Timed-out invocations |
| `archonai.model.invocation.duration.ms` | Histogram | Invocation latency |
| `archonai.model.invocation.tokens.input` | Histogram | Input token counts |
| `archonai.model.invocation.tokens.output` | Histogram | Output token counts |
| `archonai.model.invocation.tokens.total` | Counter | Cumulative token usage |

#### Gateway
| Metric | Type | Description |
|--------|------|-------------|
| `archonai.gateway.requests.total` | Counter | Total gateway requests |
| `archonai.gateway.requests.failed` | Counter | 5xx responses |
| `archonai.gateway.auth.failures` | Counter | 401 responses |
| `archonai.gateway.ratelimit.hits` | Counter | 429 responses |
| `archonai.gateway.request.duration.ms` | Histogram | End-to-end latency |

#### Connectors (per connector: Salesforce, HubSpot, QuickBooks, Slack, Google Workspace, M365)
| Metric Pattern | Type | Description |
|---------------|------|-------------|
| `archonai.{connector}.auth.attempts` | Counter | OAuth attempts |
| `archonai.{connector}.query.ops` | Counter | Read operations |
| `archonai.{connector}.write.ops` | Counter | Write operations |
| `archonai.{connector}.errors` | Counter | Errors |
| `archonai.{connector}.retries` | Counter | Retries |
| `archonai.{connector}.ratelimit.remaining` | Histogram | Rate limit headroom |

#### Intelligence Loop
| Metric | Type | Description |
|--------|------|-------------|
| `archonai.loop.cycles.completed` | Counter | Successful loop cycles |
| `archonai.loop.cycles.failures` | Counter | Failed cycles |
| `archonai.loop.cycle.duration.ms` | Histogram | Cycle duration |
| `archonai.loop.goals.generated` | Counter | Goals created |
| `archonai.loop.tasks.executed` | Counter | Tasks dispatched |

#### Runtime Health
| Metric | Type | Description |
|--------|------|-------------|
| `archonai.runtime.health.checks` | Counter | Health check runs |
| `archonai.runtime.agent.failures` | Counter | Agent failures |
| `archonai.runtime.task.timeouts` | Counter | Task timeouts |
| `archonai.runtime.queue.backlog` | Histogram | Queue depth |

#### Health Checks
| Metric | Type | Description |
|--------|------|-------------|
| `archonai.healthchecks.passed` | Counter | Passed health checks |
| `archonai.healthchecks.failed` | Counter | Failed health checks |
| `archonai.healthchecks.degraded` | Counter | Degraded health checks |

## Health Checks

### Endpoints

| Endpoint | Purpose | Auth | K8s Probe |
|----------|---------|------|-----------|
| `/healthz` | Full health report (all checks) | None | — |
| `/healthz/live` | Liveness probe (is the process alive?) | None | `livenessProbe` |
| `/healthz/ready` | Readiness probe (can it serve traffic?) | None | `readinessProbe` |
| `/health` | Legacy simple health | None (Gateway), Auth (API) | — |

### Health Check Components

| Check | Tags | What It Validates |
|-------|------|-------------------|
| `event_bus` | live, ready | Event bus connectivity |
| `task_queue` | live, ready | Queue backlog (degraded >500, unhealthy >2000) |
| `connectors` | ready | External connector availability |
| `model_providers` | ready | LLM provider consecutive failures (degraded after 3) |
| `startup` | ready | Startup component initialization (agents, tools, event bus) |

### Response Format

```json
{
  "status": "Healthy",
  "totalDurationMs": 12.3,
  "timestamp": "2026-03-18T10:00:00Z",
  "checks": [
    {
      "name": "event_bus",
      "status": "Healthy",
      "description": "Event bus is operational.",
      "durationMs": 0.5,
      "tags": ["live", "ready"]
    }
  ]
}
```

### Kubernetes Probe Configuration

```yaml
livenessProbe:
  httpGet:
    path: /healthz/live
    port: 5000
  initialDelaySeconds: 10
  periodSeconds: 15
  failureThreshold: 3

readinessProbe:
  httpGet:
    path: /healthz/ready
    port: 5000
  initialDelaySeconds: 15
  periodSeconds: 10
  failureThreshold: 3

startupProbe:
  httpGet:
    path: /healthz/ready
    port: 5000
  initialDelaySeconds: 5
  periodSeconds: 5
  failureThreshold: 30
```

## Alert-Worthy Signals

### Critical (Page)
- `archonai.gateway.requests.failed` rate > 5% of total
- `archonai.runtime.agent.failures` > 10/min
- Health check status = Unhealthy for > 2 minutes
- `archonai.model.invocations.failed` rate > 20%

### Warning
- `archonai.runtime.queue.backlog` > 500
- `archonai.{connector}.ratelimit.remaining` < 10%
- Model provider consecutive failures >= 3
- Intelligence loop cycle failures > 0

### Info
- `archonai.loop.cycles.completed` = 0 for > 10 minutes (loop may be paused)
- Rate limit hits increasing
- Auth failure spikes (potential credential stuffing)

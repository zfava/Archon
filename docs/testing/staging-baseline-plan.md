# ArchonAI Staging Performance Baseline Plan

> Converts "load tests exist" into "measured baseline exists."

## Objective

Execute the minimum credible baseline suite against staging to produce the first measured performance record. All results become the regression comparison point for every subsequent release.

## Minimum Credible Baseline Suite

Five scenarios selected for staging baseline. These cover the critical paths that enterprise due diligence will examine:

| # | Scenario | Script | Why Included | VUs | Duration |
|---|----------|--------|--------------|-----|----------|
| 1 | API CRUD | `api-crud.js` | Core read/write latency — the number every customer asks about | 100 | 5m |
| 2 | Governance/Policy Evaluation | `governance-load.js` | Approval gates are on the critical path for regulated customers | 20 | 2m |
| 3 | Multi-Tenant Isolation | `multi-tenant-isolation.js` | Zero-tolerance cross-tenant violations — non-negotiable for enterprise | 100 (10×10) | ~200 iterations |
| 4 | Connector Load | `connector-load.js` | External integration latency under concurrent provider load | 75 (ramping) | 3m30s |
| 5 | Intelligence Loop Stress | `intelligence-loop-stress.js` | Full observe/plan/execute/evaluate cycle — the differentiating workflow | 50 (ramping) | 3m |

### Scenarios Deferred from Baseline (Run After Baseline Is Established)

| Scenario | Reason for Deferral |
|----------|-------------------|
| `auth-flow.js` | Auth is prerequisite infra, not a differentiating metric |
| `gateway-throughput.js` | Rate limiting is configuration validation, not performance |
| `hero-workflow-composition.js` | Depends on all 7 services — run after individual baselines pass |
| `proof-analytics-volume.js` | High write volume test — run as follow-up stress |
| `agent-execution-stress.js` | Long-polling workflow — sensitive to environment setup |
| `connector-resilience.js` | Fault injection — run after healthy baseline exists |
| `soak-test.js` | 30-minute test — run overnight after baseline is green |

## Pass/Fail Criteria

| Scenario | Pass Condition | Abort Condition |
|----------|---------------|-----------------|
| API CRUD | Read p95 < 300ms, Write p95 < 500ms, Error < 2% | p95 breach |
| Governance | p95 < 500ms, Error < 1% | Error breach |
| Multi-Tenant Isolation | Cross-tenant violations == 0 | Any violation |
| Connector Load | p95 < 800ms, Error < 3% | p95 breach |
| Intelligence Loop | Cycle p95 < 1000ms, Error < 5% | Either breach |

## Environment Prerequisites

| Component | Required State |
|-----------|---------------|
| Gateway (YARP) | Running on port 8080, healthcheck passing at `/healthz/live` |
| API (.NET 10) | Deployed behind gateway |
| PostgreSQL 16 | Provisioned with pgvector extension, seeded with tenant data |
| NATS 2.10 | JetStream enabled |
| Test Users | Seeded: `loadtest-admin@archonai.test`, `loadtest-operator@archonai.test`, `loadtest-viewer@archonai.test` |
| Tenant Users | Seeded: `loadtest-tenant0@archonai.test` through `loadtest-tenant9@archonai.test` |
| k6 | v0.50.0+ installed on runner or via Docker `grafana/k6:0.50.0` |

## Execution Order

Run sequentially in this order. Each scenario must pass before proceeding:

1. **API CRUD** — validates basic platform responsiveness
2. **Governance** — validates policy evaluation path
3. **Multi-Tenant Isolation** — if this fails, stop everything (security issue)
4. **Connector Load** — validates external integration path
5. **Intelligence Loop** — validates the full AI workflow cycle

If multi-tenant isolation fails, do not proceed. File a P0 bug.

## Output Artifacts

Each run produces per `docs/testing/staging-baseline-results.md`:

```
results/
  api-crud-{timestamp}.json
  api-crud-{timestamp}-summary.json
  api-crud-{timestamp}.log
  governance-load-{timestamp}.json
  governance-load-{timestamp}-summary.json
  governance-load-{timestamp}.log
  multi-tenant-isolation-{timestamp}.json
  multi-tenant-isolation-{timestamp}-summary.json
  multi-tenant-isolation-{timestamp}.log
  connector-load-{timestamp}.json
  connector-load-{timestamp}-summary.json
  connector-load-{timestamp}.log
  intelligence-loop-stress-{timestamp}.json
  intelligence-loop-stress-{timestamp}-summary.json
  intelligence-loop-stress-{timestamp}.log
  summary-{timestamp}.json
```

## Baseline Ownership

| Role | Responsibility |
|------|---------------|
| Platform Lead | Triggers baseline run, reviews results |
| SRE | Validates environment prerequisites, monitors during run |
| Security | Reviews multi-tenant isolation results |
| Product | Signs off on baseline as SLA evidence |

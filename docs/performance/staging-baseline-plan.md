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

Every threshold below is enforced by k6 — the runner exits non-zero on any breach.

| Scenario | Metric | Pass (strict) | Abort (immediate) |
|----------|--------|---------------|-------------------|
| API CRUD | Read p95 | < 300ms | >= 300ms |
| API CRUD | Write p95 | < 500ms | >= 500ms |
| API CRUD | Error Rate | < 2% | >= 2% |
| Governance | p95 | < 500ms | >= 500ms |
| Governance | Error Rate | < 1% | >= 1% |
| Multi-Tenant Isolation | Cross-tenant violations | == 0 | > 0 — **STOP ALL. File P0.** |
| Multi-Tenant Isolation | p95 | < 500ms | >= 500ms |
| Multi-Tenant Isolation | Error Rate | < 2% | >= 2% |
| Connector Load | p95 | < 800ms | >= 800ms |
| Connector Load | Error Rate | < 3% | >= 3% |
| Intelligence Loop | Cycle p95 | < 1000ms | >= 1000ms |
| Intelligence Loop | Error Rate | < 5% | >= 5% |

**Regression thresholds** (applied to future runs against this baseline):

| Metric | Regression if |
|--------|--------------|
| p95 Latency | Increases by > 20% from baseline |
| Error Rate | Increases by > 0.5 percentage points from baseline |

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

Each run produces a timestamped artifact tree per `docs/performance/staging-baseline-results.md`:

```
results/baseline-{YYYYMMDD-HHMMSS}/
  baseline-summary.json                        # consolidated pass/fail + full run metadata
  scenarios/
    api-crud.json                              # k6 raw JSON output (per-request data)
    api-crud-summary.json                      # k6 summary export (aggregate metrics)
    governance-load.json
    governance-load-summary.json
    multi-tenant-isolation.json
    multi-tenant-isolation-summary.json
    connector-load.json
    connector-load-summary.json
    intelligence-loop-stress.json
    intelligence-loop-stress-summary.json
  logs/
    api-crud.log                               # stdout + stderr captured via tee
    governance-load.log
    multi-tenant-isolation.log
    connector-load.log
    intelligence-loop-stress.log
```

The `baseline-summary.json` includes: git commit SHA, branch, environment name, deployment image tags, migration version, AI provider mode, connector config mode, k6 version, and per-scenario pass/fail status.

## Baseline Ownership

| Role | Responsibility |
|------|---------------|
| Platform Lead | Triggers baseline run, reviews results |
| SRE | Validates environment prerequisites, monitors during run |
| Security | Reviews multi-tenant isolation results |
| Product | Signs off on baseline as SLA evidence |

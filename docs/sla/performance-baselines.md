# ArchonAI Performance Baselines & SLA Commitments

> Generated from k6 load test suite. All thresholds are enforced in CI.
>
> **Baseline Status**: AWAITING EXECUTION — no measured values exist yet.
> Run the baseline capture script to populate actual numbers.

## Overview

This document defines the performance baselines for ArchonAI, validated by
automated k6 load tests that run on every release branch push. These baselines
serve as SLA evidence for enterprise due diligence.

## Test Environment

| Component | Specification |
|-----------|---------------|
| Gateway | YARP reverse proxy, port 8080 |
| API | ASP.NET Core (.NET 10), port 8080 internal |
| Database | PostgreSQL 16 with pgvector |
| Event Bus | NATS 2.10 JetStream |
| Load Tool | Grafana k6 |

## SLA Commitments by Endpoint Category

### 1. Authentication Flow

| Metric | SLA Commitment | Abort on Breach | Measured Baseline |
|--------|---------------|-----------------|-------------------|
| p50 Latency | < 100ms | No | _AWAITING EXECUTION_ |
| p95 Latency | < 200ms | **Yes** | _AWAITING EXECUTION_ |
| p99 Latency | < 500ms | No | _AWAITING EXECUTION_ |
| Error Rate | < 1% | **Yes** | _AWAITING EXECUTION_ |
| Throughput | > 100 req/s | — | _AWAITING EXECUTION_ |
| VU Range | 50–100 concurrent | — | — |

**Test**: `auth-flow.js` — Full auth lifecycle (register, login, refresh, profile).

### 2. API CRUD Operations

| Metric | SLA Commitment | Abort on Breach | Measured Baseline |
|--------|---------------|-----------------|-------------------|
| Read p50 | < 150ms | No | _AWAITING EXECUTION_ |
| Read p95 | < 300ms | **Yes** | _AWAITING EXECUTION_ |
| Read p99 | < 800ms | No | _AWAITING EXECUTION_ |
| Write p50 | < 250ms | No | _AWAITING EXECUTION_ |
| Write p95 | < 500ms | **Yes** | _AWAITING EXECUTION_ |
| Write p99 | < 1200ms | No | _AWAITING EXECUTION_ |
| Error Rate | < 2% | **Yes** | _AWAITING EXECUTION_ |
| Throughput | > 100 req/s | — | _AWAITING EXECUTION_ |
| Load Profile | 100 VUs, 80/20 read/write | — | — |

**Test**: `api-crud.js` — Workflow and decision CRUD with realistic read/write ratio.

### 3. Gateway Throughput & Rate Limiting

| Rate Limit Tier | Limit | Window |
|-----------------|-------|--------|
| Standard | 120 req | 1 min |
| Admin | 60 req | 1 min |
| Connectors | 200 req | 1 min |
| Health | 300 req | 1 min |
| Metrics | 30 req | 1 min |

| Metric | SLA Commitment | Abort on Breach | Measured Baseline |
|--------|---------------|-----------------|-------------------|
| p95 Latency | < 300ms | No | _AWAITING EXECUTION_ |
| Error Rate (incl. 429s) | < 5% | No | _AWAITING EXECUTION_ |

**Validation**: Burst traffic exceeding limits must produce HTTP 429 responses.
Sustained traffic within limits must produce zero 429s.

**Test**: `gateway-throughput.js`

### 4. Multi-Tenant Isolation (Zero-Tolerance Security Guarantee)

| Metric | SLA Commitment | Abort on Breach | Measured Baseline |
|--------|---------------|-----------------|-------------------|
| Cross-Tenant Violations | **0** (zero tolerance) | **Yes** | _AWAITING EXECUTION_ |
| p95 Latency | < 500ms | No | _AWAITING EXECUTION_ |
| Error Rate | < 2% | **Yes** | _AWAITING EXECUTION_ |
| Load Profile | 10 tenants x 10 VUs = 100 concurrent | — | — |

**Zero-Tolerance Guarantee**: The multi-tenant isolation test (`multi-tenant-isolation.js`)
actively probes for cross-tenant data leakage using the following verification methods:

1. **Response field inspection**: Every API response is checked to ensure the `tenantId`
   field matches the requesting tenant's identity.
2. **Cross-tenant query injection**: The test attempts to access resources belonging to
   other tenants using manipulated query parameters.
3. **Resource scoping verification**: Agent lists, trace queries, and connector status
   endpoints are verified to return only data belonging to the requesting tenant.

Any non-zero violation count triggers:
- Immediate test abort (`abortOnFail: true` in k6 thresholds)
- Non-zero exit code from the test runner
- CI pipeline failure
- The `run-baselines.sh` script halts all remaining scenarios

This is classified as a **P0 security incident** requiring immediate investigation.
The cross-tenant isolation guarantee is non-negotiable and has no degraded-performance
fallback — it either passes with zero violations or fails entirely.

**Test reference**: `archonai/tests/load/scenarios/multi-tenant-isolation.js`

### 5. Connector Load & Retry

| Metric | SLA Commitment | Abort on Breach | Measured Baseline |
|--------|---------------|-----------------|-------------------|
| p50 Latency | < 300ms | No | _AWAITING EXECUTION_ |
| p95 Latency | < 800ms | **Yes** | _AWAITING EXECUTION_ |
| p99 Latency | < 2000ms | No | _AWAITING EXECUTION_ |
| Error Rate | < 3% | **Yes** | _AWAITING EXECUTION_ |
| Throughput | > 100 req/s | — | _AWAITING EXECUTION_ |
| Providers | Salesforce, HubSpot, QuickBooks, Slack | — | — |

**Test**: `connector-load.js` — Concurrent multi-provider operations with retry simulation.

### 6. Intelligence Loop Stress

| Metric | SLA Commitment | Abort on Breach | Measured Baseline |
|--------|---------------|-----------------|-------------------|
| Cycle p95 | < 1000ms | **Yes** | _AWAITING EXECUTION_ |
| Error Rate | < 5% | **Yes** | _AWAITING EXECUTION_ |
| Throughput | > 100 req/s | — | _AWAITING EXECUTION_ |
| Phases | Observe, Plan, Execute, Evaluate, Record | — | — |

**Test**: `intelligence-loop-stress.js` — Full observe/plan/execute/evaluate loop under load.

### 7. Hero Workflow Composition (7-Service)

| Metric | SLA Commitment | Abort on Breach | Measured Baseline |
|--------|---------------|-----------------|-------------------|
| Composition p50 | < 2000ms | No | _AWAITING EXECUTION_ |
| Composition p95 | < 5000ms | **Yes** | _AWAITING EXECUTION_ |
| Composition p99 | < 8000ms | No | _AWAITING EXECUTION_ |
| Error Rate | < 2% | **Yes** | _AWAITING EXECUTION_ |

**Services traversed per workflow**:
1. Registry (agent discovery)
2. Planner (strategy selection)
3. Scheduler (task scheduling)
4. Runtime (workflow execution)
5. Connectors (external data)
6. Reasoner (intelligence evaluation)
7. Traces (proof recording)

**Test**: `hero-workflow-composition.js`

### 8. Proof Analytics Volume

| Metric | SLA Commitment | Abort on Breach | Measured Baseline |
|--------|---------------|-----------------|-------------------|
| Write p95 | < 200ms | **Yes** | _AWAITING EXECUTION_ |
| Query p95 | < 1000ms | **Yes** | _AWAITING EXECUTION_ |
| Error Rate | < 1% | **Yes** | _AWAITING EXECUTION_ |
| Write Rate | 200 events/sec sustained | — | _AWAITING EXECUTION_ |

**Test**: `proof-analytics-volume.js` — High-volume event recording with concurrent aggregation queries.

### 9. Soak Test (Stability)

| Metric | SLA Commitment | Abort on Breach | Measured Baseline |
|--------|---------------|-----------------|-------------------|
| Duration | 30 minutes sustained | — | _AWAITING EXECUTION_ |
| VUs | 30 concurrent | — | — |
| p95 Latency | < 500ms | No | _AWAITING EXECUTION_ |
| Error Rate | < 1% | **Yes** | _AWAITING EXECUTION_ |
| Memory Growth | < 20% from baseline | — | _AWAITING EXECUTION_ |
| p95 Drift | < 50% (late vs early) | — | _AWAITING EXECUTION_ |

**Test**: `soak-test.js` — Weighted mixed workload sustained for 30 minutes with drift detection.

### 10. Governance / Policy Evaluation

| Metric | SLA Commitment | Abort on Breach | Measured Baseline |
|--------|---------------|-----------------|-------------------|
| p95 Latency | < 500ms | No | _AWAITING EXECUTION_ |
| Error Rate | < 1% | **Yes** | _AWAITING EXECUTION_ |
| Governance Eval p95 | < 200ms | No | _AWAITING EXECUTION_ |

**Test**: `governance-load.js`

### 11. Agent Execution Stress

| Metric | SLA Commitment | Abort on Breach | Measured Baseline |
|--------|---------------|-----------------|-------------------|
| p95 Latency | < 500ms | No | _AWAITING EXECUTION_ |
| Error Rate | < 1% | **Yes** | _AWAITING EXECUTION_ |
| Agent Task p95 | < 30000ms | No | _AWAITING EXECUTION_ |

**Test**: `agent-execution-stress.js`

### 12. Connector Resilience

| Metric | SLA Commitment | Abort on Breach | Measured Baseline |
|--------|---------------|-----------------|-------------------|
| p95 Latency | < 500ms | No | _AWAITING EXECUTION_ |
| Error Rate | < 1% | **Yes** | _AWAITING EXECUTION_ |

**Test**: `connector-resilience.js`

## How to Run the Baselines

### Full Baseline Capture (All 12 Scenarios)

```bash
BASE_URL=http://staging:8080 \
ADMIN_EMAIL=admin@archonai.dev \
ADMIN_PASSWORD=<staging-admin-password> \
./archonai/tests/load/run-baselines.sh
```

This runs all 12 scenarios sequentially with a 30-second cooldown between each.
Results are stored in `docs/performance/results/YYYY-MM-DD/`.

### Dry-Run Validation

```bash
BASE_URL=http://staging:8080 \
ADMIN_EMAIL=admin@archonai.dev \
ADMIN_PASSWORD=<staging-admin-password> \
./archonai/tests/load/run-baselines.sh --quick
```

Quick mode overrides all scenario durations to 30 seconds for connectivity validation.

### Single Scenario (via run-all.sh)

```bash
BASE_URL=http://staging:8080 \
./archonai/tests/load/run-all.sh auth-flow
```

### Where Results Are Stored

| Path | Contents |
|------|----------|
| `docs/performance/results/YYYY-MM-DD/baseline-summary.json` | Consolidated pass/fail with run metadata |
| `docs/performance/results/YYYY-MM-DD/scenarios/<name>.json` | Raw k6 JSON output (every metric sample) |
| `docs/performance/results/YYYY-MM-DD/scenarios/<name>-summary.json` | k6 summary export (aggregated metrics + thresholds) |
| `docs/performance/results/YYYY-MM-DD/logs/<name>.log` | Human-readable console output |

## CI Integration

Load tests run automatically via `.github/workflows/load-test.yml`:

- **Trigger**: Push to `release/**` or `main`, or manual dispatch
- **Pull requests**: Quick mode (reduced durations)
- **Artifacts**: JSON + log reports retained for 30 days
- **Summary**: GitHub Actions step summary with pass/fail counts

## Establishing & Comparing Baselines

### Record a Baseline

Run the full baseline script and store the results alongside the release tag:

```bash
BASE_URL=http://staging:8080 \
ADMIN_EMAIL=admin@archonai.dev \
ADMIN_PASSWORD=<password> \
./archonai/tests/load/run-baselines.sh
```

This produces `docs/performance/results/YYYY-MM-DD/` with every metric sample.
Tag the results directory with the commit SHA and release version.

### Compare a New Run Against Baseline

1. Run the same baseline script against the new build.

2. Extract p95 and error rate from both summary files and compare:

   ```bash
   # Compare p95 latency between baseline and current run
   BASELINE=docs/performance/results/2026-03-15/scenarios/api-crud-summary.json
   CURRENT=docs/performance/results/2026-03-20/scenarios/api-crud-summary.json

   echo "Baseline p95:"
   jq '.metrics.http_req_duration.values["p(95)"]' "$BASELINE"
   echo "Current p95:"
   jq '.metrics.http_req_duration.values["p(95)"]' "$CURRENT"
   ```

### Regression Criteria

A performance **regression** is flagged when either condition is met:

| Metric | Regression Threshold |
|--------|---------------------|
| p95 Latency | Increases by **> 20%** compared to baseline |
| Error Rate | Increases by **> 0.5 percentage points** compared to baseline |

For example, if the baseline p95 is 250ms, any run with p95 > 300ms is a
regression. If the baseline error rate is 0.8%, any run with error rate > 1.3%
is a regression.

These thresholds apply to all scenarios listed in this document. CI pipelines
should fail the build when a regression is detected.

## Methodology

- All tests use realistic workload patterns (weighted random, 80/20 splits)
- Think times simulate human interaction pauses (jittered)
- Setup/teardown phases handle test user provisioning
- Thresholds are enforced by k6 — tests exit non-zero on threshold breach
- Multi-tenant isolation uses active cross-tenant probing, not just passive checks

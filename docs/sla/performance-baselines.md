# ArchonAI Performance Baselines & SLA Commitments

> Generated from k6 load test suite. All thresholds are enforced in CI.

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

## Performance Baselines

### 1. Authentication Flow

| Metric | Target | Abort Threshold |
|--------|--------|-----------------|
| p50 Latency | < 100ms | No |
| p95 Latency | < 200ms | **Yes** |
| p99 Latency | < 500ms | No |
| Error Rate | < 1% | **Yes** |
| VU Range | 50–100 concurrent | — |

**Test**: `auth-flow.js` — Full auth lifecycle (register, login, refresh, profile).

### 2. API CRUD Operations

| Metric | Target | Abort Threshold |
|--------|--------|-----------------|
| Read p50 | < 150ms | No |
| Read p95 | < 300ms | **Yes** |
| Read p99 | < 800ms | No |
| Write p50 | < 250ms | No |
| Write p95 | < 500ms | **Yes** |
| Write p99 | < 1200ms | No |
| Error Rate | < 2% | **Yes** |
| Load Profile | 100 VUs, 80/20 read/write | — |

**Test**: `api-crud.js` — Workflow and decision CRUD with realistic read/write ratio.

### 3. Gateway Throughput & Rate Limiting

| Rate Limit Tier | Limit | Window |
|-----------------|-------|--------|
| Standard | 120 req | 1 min |
| Admin | 60 req | 1 min |
| Connectors | 200 req | 1 min |
| Health | 300 req | 1 min |
| Metrics | 30 req | 1 min |

**Validation**: Burst traffic exceeding limits must produce HTTP 429 responses.
Sustained traffic within limits must produce zero 429s.

**Test**: `gateway-throughput.js`

### 4. Multi-Tenant Isolation

| Metric | Target | Abort Threshold |
|--------|--------|-----------------|
| Cross-Tenant Violations | **0** (zero tolerance) | **Yes** |
| p95 Latency | < 500ms | No |
| Error Rate | < 2% | **Yes** |
| Load Profile | 10 tenants × 10 VUs = 100 concurrent | — |

**Verification method**: Every API response is inspected for `tenantId` field
consistency. Cross-tenant query parameter injection is tested.

**Test**: `multi-tenant-isolation.js`

### 5. Connector Load & Retry

| Metric | Target | Abort Threshold |
|--------|--------|-----------------|
| p50 Latency | < 300ms | No |
| p95 Latency | < 800ms | **Yes** |
| p99 Latency | < 2000ms | No |
| Error Rate | < 3% | **Yes** |
| Providers | Salesforce, HubSpot, QuickBooks, Slack | — |

**Test**: `connector-load.js` — Concurrent multi-provider operations with retry simulation.

### 6. Intelligence Loop Stress

| Metric | Target | Abort Threshold |
|--------|--------|-----------------|
| Cycle p95 | < 1000ms | **Yes** |
| Error Rate | < 5% | **Yes** |
| Phases | Observe → Plan → Execute → Evaluate → Record | — |

**Test**: `intelligence-loop-stress.js` — Full observe/plan/execute/evaluate loop under load.

### 7. Hero Workflow Composition (7-Service)

| Metric | Target | Abort Threshold |
|--------|--------|-----------------|
| Composition p50 | < 2000ms | No |
| Composition p95 | < 5000ms | **Yes** |
| Composition p99 | < 8000ms | No |
| Error Rate | < 2% | **Yes** |

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

| Metric | Target | Abort Threshold |
|--------|--------|-----------------|
| Write p95 | < 200ms | **Yes** |
| Query p95 | < 1000ms | **Yes** |
| Error Rate | < 1% | **Yes** |
| Write Rate | 200 events/sec sustained | — |

**Test**: `proof-analytics-volume.js` — High-volume event recording with concurrent aggregation queries.

### 9. Soak Test (Stability)

| Metric | Target | Abort Threshold |
|--------|--------|-----------------|
| Duration | 30 minutes sustained | — |
| VUs | 30 concurrent | — |
| p95 Latency | < 500ms | No |
| Error Rate | < 1% | **Yes** |
| Memory Growth | < 20% from baseline | — |
| p95 Drift | < 50% (late vs early) | — |

**Test**: `soak-test.js` — Weighted mixed workload sustained for 30 minutes with drift detection.

## CI Integration

Load tests run automatically via `.github/workflows/load-test.yml`:

- **Trigger**: Push to `release/**` or `main`, or manual dispatch
- **Pull requests**: Quick mode (reduced durations)
- **Artifacts**: JSON + log reports retained for 30 days
- **Summary**: GitHub Actions step summary with pass/fail counts

## Report Format

Each scenario produces:
- `{scenario}-{timestamp}.json` — Full k6 JSON output (machine-readable)
- `{scenario}-{timestamp}-summary.json` — k6 summary export (metrics + thresholds)
- `{scenario}-{timestamp}.log` — Human-readable console output
- `summary-{timestamp}.json` — Consolidated pass/fail summary

## Methodology

- All tests use realistic workload patterns (weighted random, 80/20 splits)
- Think times simulate human interaction pauses (jittered)
- Setup/teardown phases handle test user provisioning
- Thresholds are enforced by k6 — tests exit non-zero on threshold breach
- Multi-tenant isolation uses active cross-tenant probing, not just passive checks

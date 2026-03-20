# Staging Baseline Results

> **Status**: AWAITING EXECUTION
>
> This document is the report template. Fill in measured values after running
> the baseline suite per `docs/performance/staging-baseline-runbook.md`. Do not invent numbers.

## Run Metadata

| Field | Value |
|-------|-------|
| Date | _YYYY-MM-DD HH:MM UTC_ |
| Commit SHA | _full 40-char SHA from `git rev-parse HEAD`_ |
| Branch | _release/x.y.z_ |
| Environment | _staging / staging-2 / pre-prod_ |
| Deployment Image Tags | _e.g. ghcr.io/archonai/api:1.2.3-rc1, ghcr.io/archonai/gateway:1.2.3-rc1_ |
| Migration/Schema Version | _e.g. 20260315_001 — from last applied migration_ |
| AI Provider Mode | _cloud-openai / cloud-anthropic / azure-openai / local-ollama / mixed_ |
| Connector Config Mode | _live-sandbox / mock-stub / hybrid (specify per provider)_ |
| Gateway Host | _staging-gateway.internal:8080_ |
| k6 Version | _0.50.0_ |
| Runner | _host or Docker_ |
| Operator | _name_ |

## Environment Assumptions

| Component | Specification | Verified |
|-----------|---------------|----------|
| Gateway | YARP reverse proxy, port 8080 | [ ] |
| API | ASP.NET Core (.NET 10) | [ ] |
| PostgreSQL | 16 with pgvector, seeded | [ ] |
| NATS | 2.10 JetStream enabled | [ ] |
| Test Users | 3 role-based + 10 tenant users seeded | [ ] |
| Network | k6 runner co-located or <1ms RTT to gateway | [ ] |

---

## 1. API CRUD

| Metric | Target | Measured | Pass/Fail |
|--------|--------|----------|-----------|
| Read p50 | < 150ms | _ms_ | |
| Read p95 | < 300ms | _ms_ | |
| Read p99 | < 800ms | _ms_ | |
| Write p50 | < 250ms | _ms_ | |
| Write p95 | < 500ms | _ms_ | |
| Write p99 | < 1200ms | _ms_ | |
| Error Rate | < 2% | _% | |
| Total Requests | — | _ | |
| Throughput (req/s) | — | _ | |
| VUs | 100 | 100 | |
| Duration | 5m | _m_ | |

**Bottleneck observations**: _none yet_

---

## 2. Governance / Policy Evaluation

| Metric | Target | Measured | Pass/Fail |
|--------|--------|----------|-----------|
| p50 | < 250ms | _ms_ | |
| p95 | < 500ms | _ms_ | |
| p99 | < 2000ms | _ms_ | |
| Error Rate | < 1% | _% | |
| Governance Throughput (decisions/s) | — | _ | |
| Approval Latency p95 | < 200ms | _ms_ | |
| Cross-Tenant Blocked | >0 expected | _ | |
| Total Requests | — | _ | |
| VUs | 20 | 20 | |
| Duration | 2m | _m_ | |

**Bottleneck observations**: _none yet_

---

## 3. Multi-Tenant Isolation

| Metric | Target | Measured | Pass/Fail |
|--------|--------|----------|-----------|
| Cross-Tenant Violations | **0** | _ | |
| p95 Latency | < 500ms | _ms_ | |
| Error Rate | < 2% | _% | |
| Tenants Tested | 10 | _ | |
| VUs per Tenant | 10 | _ | |
| Iterations per VU | 20 | _ | |
| Total Requests | — | _ | |

### Tenant Isolation Findings

| Check | Result |
|-------|--------|
| Response `tenantId` matches request tenant | _PASS/FAIL_ |
| Cross-tenant query injection blocked | _PASS/FAIL_ |
| Agent list scoped to tenant | _PASS/FAIL_ |
| Trace query scoped to tenant | _PASS/FAIL_ |
| Connector status scoped to tenant | _PASS/FAIL_ |

**If any violation detected**: _describe the exact request/response pair, tenant IDs involved, and endpoint_

---

## 4. Connector Load

| Metric | Target | Measured | Pass/Fail |
|--------|--------|----------|-----------|
| p50 | < 300ms | _ms_ | |
| p95 | < 800ms | _ms_ | |
| p99 | < 2000ms | _ms_ | |
| Error Rate | < 3% | _% | |
| Retries Triggered | — | _ | |
| Total Requests | — | _ | |
| Throughput (req/s) | — | _ | |

### Per-Provider Breakdown

| Provider | Requests | p95 | Errors | Retries |
|----------|----------|-----|--------|---------|
| Salesforce | _ | _ms_ | _ | _ |
| HubSpot | _ | _ms_ | _ | _ |
| QuickBooks | _ | _ms_ | _ | _ |
| Slack | _ | _ms_ | _ | _ |

**Bottleneck observations**: _none yet_

---

## 5. Intelligence Loop Stress

| Metric | Target | Measured | Pass/Fail |
|--------|--------|----------|-----------|
| Cycle p95 | < 1000ms | _ms_ | |
| Error Rate | < 5% | _% | |
| Workflows Started | — | _ | |
| Workflows Completed | — | _ | |
| Completion Rate | — | _% | |
| Total Requests | — | _ | |

### Per-Phase Breakdown

| Phase | p50 | p95 | Errors |
|-------|-----|-----|--------|
| Observe (agent registry) | _ms_ | _ms_ | _ |
| Plan (agent selection) | _ms_ | _ms_ | _ |
| Execute (strategy eval) | _ms_ | _ms_ | _ |
| Evaluate (pattern analysis) | _ms_ | _ms_ | _ |
| Record (trace audit) | _ms_ | _ms_ | _ |

**Bottleneck observations**: _none yet_

---

## Summary

| Scenario | Status | Key Metric | Value |
|----------|--------|------------|-------|
| API CRUD | _PENDING_ | Read p95 | _ms_ |
| Governance | _PENDING_ | Approval p95 | _ms_ |
| Multi-Tenant Isolation | _PENDING_ | Violations | _ |
| Connector Load | _PENDING_ | p95 | _ms_ |
| Intelligence Loop | _PENDING_ | Cycle p95 | _ms_ |

## Errors and Timeouts

_List any HTTP errors, connection timeouts, or unexpected status codes observed during the run. Include counts and affected endpoints._

| Error Type | Count | Affected Endpoint(s) | Notes |
|------------|-------|---------------------|-------|
| _e.g. 500_ | _ | _ | _ |
| _e.g. timeout_ | _ | _ | _ |

## Next Tuning Priorities

_After baseline is measured, rank these by impact:_

1. _Highest-latency endpoint identified_
2. _Highest error-rate scenario_
3. _Any scenario near threshold (within 20% of limit)_
4. _Database query optimization candidates_
5. _Connection pool sizing adjustments_

## Regression Comparison Reference

Once this baseline is recorded, future runs compare against these values:

| Metric | Regression Threshold |
|--------|---------------------|
| p95 Latency | Increases by > 20% from this baseline |
| Error Rate | Increases by > 0.5 percentage points from this baseline |

---

_This document is the single source of truth for the first measured baseline.
Update it only with actual measured values. Never fill in estimated or projected numbers._

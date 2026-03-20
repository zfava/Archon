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

## How to Populate This Document

Run the full baseline capture script against a staging environment:

```bash
# 1. Ensure staging is deployed and healthy
curl -sf http://staging:8080/healthz/live

# 2. Run all 12 scenarios with baseline capture
BASE_URL=http://staging:8080 \
ADMIN_EMAIL=admin@archonai.dev \
ADMIN_PASSWORD=<staging-admin-password> \
./archonai/tests/load/run-baselines.sh

# 3. For a dry-run first (30s durations to validate connectivity):
BASE_URL=http://staging:8080 \
ADMIN_EMAIL=admin@archonai.dev \
ADMIN_PASSWORD=<staging-admin-password> \
./archonai/tests/load/run-baselines.sh --quick
```

Results are written to `docs/performance/results/YYYY-MM-DD/`. After the run:

1. Open `docs/performance/results/YYYY-MM-DD/baseline-summary.json` for the consolidated pass/fail.
2. Open each `scenarios/<name>-summary.json` for per-scenario p50/p95/p99 and error rates.
3. Copy the measured values into the tables below, replacing the `_placeholder_` values.
4. Update the Run Metadata table with the exact commit SHA, branch, environment, and image tags.
5. Change the Status at the top from "AWAITING EXECUTION" to "BASELINE RECORDED" with the date.

---

## Threshold Definitions

Each threshold maps to an SLA commitment defined in `archonai/tests/load/lib/thresholds.js`.
k6 enforces these at runtime — a scenario exits non-zero if any threshold is breached.

| Scenario | Metric | Threshold | Abort on Fail | Rationale |
|----------|--------|-----------|---------------|-----------|
| **Auth Flow** | p95 latency | < 200ms | Yes | Login/refresh must be near-instant for UX; users abandon at >500ms. |
| **Auth Flow** | Error rate | < 1% | Yes | Auth failures block all downstream functionality. |
| **API CRUD** | Read p95 | < 300ms | Yes | Core read path; interactive UI depends on sub-300ms reads. |
| **API CRUD** | Write p95 | < 500ms | Yes | Write operations tolerate slightly more latency; 500ms keeps UI responsive. |
| **API CRUD** | Error rate | < 2% | Yes | CRUD errors directly impact data integrity and user trust. |
| **Gateway** | p95 latency | < 300ms | No | Gateway adds routing overhead; 300ms budget leaves room for downstream. |
| **Gateway** | Error rate | < 5% | No | Includes expected 429 rate-limit responses; higher tolerance by design. |
| **Multi-Tenant** | Cross-tenant violations | == 0 | Yes | **Zero tolerance.** Any cross-tenant data leakage is a P0 security incident. |
| **Multi-Tenant** | p95 latency | < 500ms | No | Tenant-scoped queries should not add significant overhead vs. single-tenant. |
| **Multi-Tenant** | Error rate | < 2% | Yes | Errors in tenant isolation could mask security issues. |
| **Connector** | p95 latency | < 800ms | Yes | External provider round-trips (Salesforce, HubSpot, etc.) are inherently slower. |
| **Connector** | Error rate | < 3% | Yes | External APIs have variable reliability; 3% accommodates transient failures. |
| **Intelligence Loop** | Cycle p95 | < 1000ms | Yes | Full observe/plan/execute/evaluate cycle; 1s keeps AI loop interactive. |
| **Intelligence Loop** | Error rate | < 5% | Yes | AI operations have higher variance; 5% allows for model timeouts. |
| **Hero Workflow** | Composition p95 | < 5000ms | Yes | 7-service traversal; 5s total budget (~700ms per service hop). |
| **Hero Workflow** | Error rate | < 2% | Yes | End-to-end workflow failure directly impacts business outcomes. |
| **Proof Analytics** | Write p95 | < 200ms | Yes | High-volume event ingestion; must keep up with 200 events/sec sustained. |
| **Proof Analytics** | Query p95 | < 1000ms | Yes | Aggregation queries over large datasets; 1s keeps dashboards responsive. |
| **Proof Analytics** | Error rate | < 1% | Yes | Audit trail integrity requires near-zero data loss. |
| **Soak Test** | p95 latency | < 500ms | No | 30-minute sustained load; detects memory leaks and connection pool exhaustion. |
| **Soak Test** | Error rate | < 1% | Yes | Extended stability requires consistently low error rates. |

---

## 1. Authentication Flow

| Metric | Target | Measured | Pass/Fail |
|--------|--------|----------|-----------|
| p50 | < 100ms | _ms_ | |
| p95 | < 200ms | _ms_ | |
| p99 | < 500ms | _ms_ | |
| Error Rate | < 1% | _% | |
| Total Requests | — | _ | |
| Throughput (req/s) | — | _ | |
| VUs | 50–100 | _ | |

**Bottleneck observations**: _none yet_

---

## 2. API CRUD

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

## 3. Gateway Throughput & Rate Limiting

| Metric | Target | Measured | Pass/Fail |
|--------|--------|----------|-----------|
| p95 | < 300ms | _ms_ | |
| Error Rate (incl. 429s) | < 5% | _% | |
| 429 Rate-Limited Count | — | _ | |
| Total Requests | — | _ | |
| Throughput (req/s) | — | _ | |

**Bottleneck observations**: _none yet_

---

## 4. Multi-Tenant Isolation

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

## 5. Connector Load

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

## 6. Intelligence Loop Stress

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

## 7. Hero Workflow Composition (7-Service)

| Metric | Target | Measured | Pass/Fail |
|--------|--------|----------|-----------|
| Composition p50 | < 2000ms | _ms_ | |
| Composition p95 | < 5000ms | _ms_ | |
| Composition p99 | < 8000ms | _ms_ | |
| Error Rate | < 2% | _% | |
| Total Requests | — | _ | |

**Bottleneck observations**: _none yet_

---

## 8. Proof Analytics Volume

| Metric | Target | Measured | Pass/Fail |
|--------|--------|----------|-----------|
| Write p95 | < 200ms | _ms_ | |
| Query p95 | < 1000ms | _ms_ | |
| Error Rate | < 1% | _% | |
| Write Rate (events/s) | 200 sustained | _ | |
| Total Requests | — | _ | |

**Bottleneck observations**: _none yet_

---

## 9. Soak Test (Stability)

| Metric | Target | Measured | Pass/Fail |
|--------|--------|----------|-----------|
| Duration | 30 min sustained | _m_ | |
| VUs | 30 concurrent | _ | |
| p95 Latency | < 500ms | _ms_ | |
| Error Rate | < 1% | _% | |
| Memory Growth | < 20% from start | _% | |
| p95 Drift (late vs early) | < 50% | _% | |
| Total Requests | — | _ | |

**Bottleneck observations**: _none yet_

---

## 10. Governance / Policy Evaluation

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

## 11. Agent Execution Stress

| Metric | Target | Measured | Pass/Fail |
|--------|--------|----------|-----------|
| p95 Latency | < 500ms | _ms_ | |
| Error Rate | < 1% | _% | |
| Total Requests | — | _ | |
| Throughput (req/s) | — | _ | |

**Bottleneck observations**: _none yet_

---

## 12. Connector Resilience

| Metric | Target | Measured | Pass/Fail |
|--------|--------|----------|-----------|
| p95 Latency | < 500ms | _ms_ | |
| Error Rate | < 1% | _% | |
| Total Requests | — | _ | |
| Retry Success Rate | — | _% | |

**Bottleneck observations**: _none yet_

---

## Summary

| Scenario | Status | Key Metric | Value |
|----------|--------|------------|-------|
| Auth Flow | _PENDING_ | p95 | _ms_ |
| API CRUD | _PENDING_ | Read p95 | _ms_ |
| Gateway Throughput | _PENDING_ | p95 | _ms_ |
| Multi-Tenant Isolation | _PENDING_ | Violations | _ |
| Connector Load | _PENDING_ | p95 | _ms_ |
| Intelligence Loop | _PENDING_ | Cycle p95 | _ms_ |
| Hero Workflow | _PENDING_ | Composition p95 | _ms_ |
| Proof Analytics | _PENDING_ | Write p95 | _ms_ |
| Soak Test | _PENDING_ | p95 | _ms_ |
| Governance | _PENDING_ | Approval p95 | _ms_ |
| Agent Execution | _PENDING_ | p95 | _ms_ |
| Connector Resilience | _PENDING_ | p95 | _ms_ |

## Errors and Timeouts

_List any HTTP errors, connection timeouts, or unexpected status codes observed during the run. Include counts and affected endpoints._

| Error Type | Count | Affected Endpoint(s) | Notes |
|------------|-------|---------------------|-------|
| _e.g. 500_ | _ | _ | _ |
| _e.g. timeout_ | _ | _ | _ |

## Regression Detection

Once this baseline is recorded, future runs are compared against these values to detect
performance regressions. The comparison process:

### How to Compare

1. **Run the same baseline script** against the new build:

   ```bash
   BASE_URL=http://staging:8080 \
   ADMIN_EMAIL=admin@archonai.dev \
   ADMIN_PASSWORD=<password> \
   ./archonai/tests/load/run-baselines.sh
   ```

2. **Compare summary files** side by side. Each run produces per-scenario summary JSONs
   in `docs/performance/results/YYYY-MM-DD/scenarios/<name>-summary.json`. Extract the
   key metrics:

   ```bash
   # Compare p95 latency between baseline and current run
   BASELINE_DIR=docs/performance/results/2026-03-15   # date of baseline
   CURRENT_DIR=docs/performance/results/2026-03-20    # date of current run

   for scenario in auth-flow api-crud gateway-throughput multi-tenant-isolation \
     connector-load intelligence-loop-stress hero-workflow-composition \
     proof-analytics-volume soak-test governance-load agent-execution-stress \
     connector-resilience; do
     echo "--- $scenario ---"
     echo -n "  Baseline p95: "
     jq -r '.metrics.http_req_duration.values["p(95)"] // "N/A"' \
       "$BASELINE_DIR/scenarios/${scenario}-summary.json" 2>/dev/null || echo "N/A"
     echo -n "  Current  p95: "
     jq -r '.metrics.http_req_duration.values["p(95)"] // "N/A"' \
       "$CURRENT_DIR/scenarios/${scenario}-summary.json" 2>/dev/null || echo "N/A"
   done
   ```

### Regression Criteria

A performance **regression** is flagged when either condition is met:

| Metric | Regression Threshold |
|--------|---------------------|
| p95 Latency | Increases by **> 20%** compared to baseline |
| Error Rate | Increases by **> 0.5 percentage points** compared to baseline |

For example, if the baseline p95 is 250ms, any run with p95 > 300ms is a regression.
If the baseline error rate is 0.8%, any run with error rate > 1.3% is a regression.

### Special Cases

- **Multi-Tenant Isolation**: Any non-zero cross-tenant violation count is an immediate
  P0 security incident regardless of regression percentage.
- **Soak Test**: Additionally check for p95 drift (late-run p95 vs early-run p95 > 50%)
  and memory growth (> 20% from initial measurement).
- **Gateway**: The 429 error count should be analyzed separately from true errors since
  rate-limiting responses are expected behavior.

### CI Integration

The GitHub Actions workflow at `.github/workflows/load-test.yml` runs load tests
automatically on `release/**` and `main` branch pushes. k6 threshold enforcement
means the pipeline fails if any SLA is breached, providing automatic regression
detection without manual comparison.

## Next Tuning Priorities

_After baseline is measured, rank these by impact:_

1. _Highest-latency endpoint identified_
2. _Highest error-rate scenario_
3. _Any scenario near threshold (within 20% of limit)_
4. _Database query optimization candidates_
5. _Connection pool sizing adjustments_

---

_This document is the single source of truth for the first measured baseline.
Update it only with actual measured values. Never fill in estimated or projected numbers._

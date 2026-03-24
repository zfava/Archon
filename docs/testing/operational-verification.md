# Operational Verification Guide

This document describes how to verify ArchonAI's operational behavior in pre-production and production environments.

## Overview

ArchonAI uses a layered verification strategy:

1. **Unit tests** — Logic correctness (no infrastructure)
2. **Integration tests** — Component interactions with real databases (Testcontainers)
3. **Runtime proof tests** — Operational behavior verification against production code paths
4. **Operational smoke tests** — Post-deployment verification against live infrastructure

This document focuses on layers 3 and 4.

## Runtime Proof Test Execution

### Prerequisites

- .NET 10 SDK
- Docker (for Testcontainers-based tests)

### Running All Runtime Proof Tests

```bash
cd archonai
dotnet test --filter "Category=RuntimeProof" --logger "console;verbosity=detailed"
```

### Running by Subsystem

```bash
# Worker health probes (no Docker needed)
dotnet test --filter "Category=RuntimeProof&Subsystem=WorkerHealth"

# Retention service (Docker required)
dotnet test --filter "Category=RuntimeProof&Subsystem=Retention"

# Governance lifecycle (Docker required)
dotnet test --filter "Category=RuntimeProof&Subsystem=Governance"
```

## What the Runtime Proof Tests Verify

### 1. Worker Health Endpoint Behavior

**What is proven:**
- Liveness probes return alive regardless of readiness state (prevents unnecessary pod restarts)
- Readiness probes return not-ready (503) when any dependency is unhealthy
- Readiness probes respect the 50ms timeout budget
- Startup readiness blocks until all components (AgentRegistration, ToolRegistration, EventBus) signal ready
- Task queue health degrades at 500 items, fails at 2000 items
- Connector health aggregates individual connector states
- Model provider health degrades after 3 consecutive failures and recovers on success

**Production impact:** These behaviors determine Kubernetes pod lifecycle and load balancer routing decisions.

### 2. Retention Service Execution

**What is proven:**
- Retention sweep correctly identifies and deletes rows older than the configured retention period
- Rows within the retention window are preserved (no data loss)
- Empty tables produce zero-delete sweeps without errors
- Sweep results are recorded in the `retention_log` table for audit
- Missing connection string produces a graceful no-op (not a crash)

**Production impact:** Retention sweep runs daily at 02:00 UTC. Incorrect behavior could delete recent data or fail to clean up stale data, affecting storage costs and compliance.

### 3. Governance Approval Lifecycle

**What is proven:**
- Approval gates survive service restarts (data persisted to PostgreSQL)
- Separation of duties is enforced: requesters cannot approve their own requests
- Role-based constraints are enforced: only the required role can approve
- Already-reviewed gates cannot be reviewed again (double-review prevention)
- Execution results (success and failure with error messages) persist across restarts
- Multi-tenant isolation holds under concurrent governance operations
- Approval policies persist and enforce constraints after service restart

**Production impact:** Governance gates protect high-risk operations (workflow cancellation, policy deletion, RBAC changes, connector disconnection, strategy overrides). Enforcement failures would allow unauthorized operational changes.

## Post-Deployment Operational Smoke Tests

After deploying to a live environment, verify the following manually or via automation:

### Health Endpoints

```bash
# API service health (port 8080)
curl -sf http://api-host:8080/healthz | jq .

# Worker service liveness (port 8081)
curl -sf http://worker-host:8081/healthz/live | jq .

# Worker service readiness (port 8081)
curl -sf http://worker-host:8081/healthz/ready | jq .
```

**Expected:** All return HTTP 200 with `status: "alive"` or `status: "ready"`.

### Database Connectivity

```bash
# Verify the API can reach PostgreSQL
curl -sf http://api-host:8080/healthz | jq '.entries.database'
```

### Governance Subsystem

```bash
# List approval policies (should return seeded defaults)
curl -sf -H "Authorization: Bearer $TOKEN" \
  http://api-host:8080/api/governance/policies | jq .

# Verify approval history endpoint responds
curl -sf -H "Authorization: Bearer $TOKEN" \
  http://api-host:8080/api/governance/approvals/history?limit=5 | jq .
```

### Retention Service

```bash
# Check retention_log for recent sweep entries
psql "$DATABASE_URL" -c \
  "SELECT ran_at_utc, audit_rows_deleted, telemetry_rows_deleted, duration_ms
   FROM archonai.retention_log ORDER BY ran_at_utc DESC LIMIT 5;"
```

**Expected:** At least one row per day (sweep runs at 02:00 UTC).

## CI/CD Integration

Add the runtime proof tests to the CI pipeline as a gate:

```yaml
# .github/workflows/ci-cd.yml
runtime-proof:
  runs-on: ubuntu-latest
  services:
    docker:
      image: docker:dind
  steps:
    - uses: actions/checkout@v4
    - uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '10.0.x'
    - name: Run runtime proof tests
      run: |
        cd archonai
        dotnet test --filter "Category=RuntimeProof" \
          --logger "trx;LogFileName=runtime-proof.trx" \
          --results-directory ./TestResults
    - uses: actions/upload-artifact@v4
      with:
        name: runtime-proof-results
        path: archonai/TestResults/
```

## Verification Matrix

| Environment | Health Endpoints | Retention | Governance | Method |
|-------------|-----------------|-----------|------------|--------|
| CI (PR gate) | Automated (unit) | Automated (Testcontainers) | Automated (Testcontainers) | `dotnet test --filter "Category=RuntimeProof"` |
| Staging | Automated (curl) | Manual (psql query) | Automated (API calls) | Post-deploy smoke test |
| Production | Kubernetes probes | Retention log monitoring | Audit log monitoring | Grafana dashboards + alerts |

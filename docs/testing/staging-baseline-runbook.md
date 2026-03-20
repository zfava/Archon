# Staging Baseline Runbook

> Copy-paste commands. No interpretation required.

## Prerequisites Check

```bash
# 1. Verify staging gateway is reachable
curl -sf http://STAGING_HOST:8080/healthz/live && echo "OK" || echo "FAIL: Gateway not ready"

# 2. Verify k6 is installed
k6 version  # Expect: k6 v0.50.0 or later

# 3. If k6 not installed — use Docker
docker run --rm grafana/k6:0.50.0 version
```

## Environment Variables

Set these before running. Replace placeholders with actual staging values:

```bash
export BASE_URL="http://STAGING_HOST:8080"
export REPORT_DIR="./results/baseline-$(date +%Y%m%d)"
export ADMIN_EMAIL="loadtest-admin@archonai.test"
export ADMIN_PASSWORD="LoadTest!Admin#2026"
export OPERATOR_EMAIL="loadtest-operator@archonai.test"
export OPERATOR_PASSWORD="LoadTest!Operator#2026"
export VIEWER_EMAIL="loadtest-viewer@archonai.test"
export VIEWER_PASSWORD="LoadTest!Viewer#2026"
export TENANT_COUNT="10"
mkdir -p "$REPORT_DIR"
```

## Option A: Run via Baseline Script (Recommended)

```bash
cd archonai/tests/load
chmod +x run-baseline.sh
BASE_URL="http://STAGING_HOST:8080" ./run-baseline.sh
```

The script runs the 5 baseline scenarios in order, stops on multi-tenant failure, and produces all artifacts.

## Option B: Run Individual Scenarios Manually

### 1. API CRUD

```bash
k6 run \
  --out "json=$REPORT_DIR/api-crud.json" \
  --summary-export "$REPORT_DIR/api-crud-summary.json" \
  --env "BASE_URL=$BASE_URL" \
  archonai/tests/load/scenarios/api-crud.js \
  2>&1 | tee "$REPORT_DIR/api-crud.log"
```

**Check**: Read p95 < 300ms, Write p95 < 500ms, Error < 2%

### 2. Governance/Policy Evaluation

```bash
k6 run \
  --out "json=$REPORT_DIR/governance-load.json" \
  --summary-export "$REPORT_DIR/governance-load-summary.json" \
  --env "BASE_URL=$BASE_URL" \
  archonai/tests/load/scenarios/governance-load.js \
  2>&1 | tee "$REPORT_DIR/governance-load.log"
```

**Check**: p95 < 500ms, Error < 1%

### 3. Multi-Tenant Isolation (CRITICAL — stop if this fails)

```bash
k6 run \
  --out "json=$REPORT_DIR/multi-tenant-isolation.json" \
  --summary-export "$REPORT_DIR/multi-tenant-isolation-summary.json" \
  --env "BASE_URL=$BASE_URL" \
  --env "TENANT_COUNT=10" \
  archonai/tests/load/scenarios/multi-tenant-isolation.js \
  2>&1 | tee "$REPORT_DIR/multi-tenant-isolation.log"
```

**Check**: `archon_cross_tenant_violations` == 0. If non-zero, STOP. File P0.

### 4. Connector Load

```bash
k6 run \
  --out "json=$REPORT_DIR/connector-load.json" \
  --summary-export "$REPORT_DIR/connector-load-summary.json" \
  --env "BASE_URL=$BASE_URL" \
  archonai/tests/load/scenarios/connector-load.js \
  2>&1 | tee "$REPORT_DIR/connector-load.log"
```

**Check**: p95 < 800ms, Error < 3%

### 5. Intelligence Loop Stress

```bash
k6 run \
  --out "json=$REPORT_DIR/intelligence-loop-stress.json" \
  --summary-export "$REPORT_DIR/intelligence-loop-stress-summary.json" \
  --env "BASE_URL=$BASE_URL" \
  archonai/tests/load/scenarios/intelligence-loop-stress.js \
  2>&1 | tee "$REPORT_DIR/intelligence-loop-stress.log"
```

**Check**: Cycle p95 < 1000ms, Error < 5%

## Option C: Run via Docker (No Local k6)

```bash
cd archonai
docker compose -f docker-compose.yml -f tests/load/docker-compose.load-test.yml run \
  -e BASE_URL=http://gateway:8080 \
  k6-runner
```

Or run a single scenario via Docker:

```bash
docker run --rm \
  -v "$(pwd)/archonai/tests/load:/load-tests" \
  -v "$(pwd)/results:/results" \
  -w /load-tests \
  -e BASE_URL="http://STAGING_HOST:8080" \
  grafana/k6:0.50.0 run \
    --out "json=/results/api-crud.json" \
    --summary-export "/results/api-crud-summary.json" \
    scenarios/api-crud.js
```

## Post-Run: Extract Results

```bash
# Extract p95 from summary JSON
for f in "$REPORT_DIR"/*-summary.json; do
  echo "=== $(basename "$f") ==="
  jq '{
    p50: .metrics.http_req_duration.values["p(50)"],
    p95: .metrics.http_req_duration.values["p(95)"],
    p99: .metrics.http_req_duration.values["p(99)"],
    error_rate: .metrics.http_req_failed.values.rate,
    total_requests: .metrics.http_reqs.values.count
  }' "$f"
done

# Check for cross-tenant violations
jq '.metrics.archon_cross_tenant_violations.values.count // 0' \
  "$REPORT_DIR/multi-tenant-isolation-summary.json"
```

## Post-Run: Record Baseline

After a successful run, copy results into the repository:

```bash
# Tag the baseline
COMMIT_SHA=$(git rev-parse --short HEAD)
cp -r "$REPORT_DIR" "docs/testing/baselines/$COMMIT_SHA/"
git add "docs/testing/baselines/$COMMIT_SHA/"
git commit -m "perf: record staging baseline at $COMMIT_SHA"
```

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| `connection refused` on BASE_URL | Gateway not running or wrong port. Check `curl $BASE_URL/healthz/live` |
| All requests return 401 | Test users not seeded. Run database seed script first |
| Cross-tenant violations > 0 | Security bug. Stop all testing. File P0 with the violation log |
| k6 exits with threshold breach | Read the log to find which threshold failed. Compare against targets in plan |
| `ECONNRESET` / timeouts | Check network path between k6 runner and staging. May need VPN/firewall rules |

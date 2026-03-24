#!/usr/bin/env bash
# ArchonAI Staging Baseline Runner
# Runs the 5 minimum credible baseline scenarios in order.
# Stops immediately if multi-tenant isolation fails (P0 security issue).
#
# Usage:
#   BASE_URL=http://staging:8080 ./run-baseline.sh
#   BASE_URL=http://staging:8080 ./run-baseline.sh --quick   # 30s durations for dry-run
#
# Artifact tree produced:
#   $RESULTS_DIR/
#     baseline-summary.json          # consolidated pass/fail + run metadata
#     scenarios/
#       api-crud.json                # k6 raw JSON output
#       api-crud-summary.json        # k6 summary export
#       governance-load.json
#       governance-load-summary.json
#       ...
#     logs/
#       api-crud.log                 # stdout + stderr per scenario
#       governance-load.log
#       ...

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
RESULTS_DIR="${REPORT_DIR:-$SCRIPT_DIR/results/baseline-$TIMESTAMP}"
BASE_URL="${BASE_URL:-http://localhost:8080}"
QUICK_MODE="${1:-}"

# ── Capture run identity metadata ────────────────────────────────
GIT_SHA=$(git rev-parse HEAD 2>/dev/null || echo "unknown")
GIT_BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "unknown")
K6_VER=$(k6 version 2>/dev/null || echo "unknown")
ENVIRONMENT_NAME="${ENVIRONMENT_NAME:-unknown}"
DEPLOY_IMAGE_TAG="${DEPLOY_IMAGE_TAG:-unknown}"
MIGRATION_VERSION="${MIGRATION_VERSION:-unknown}"
AI_PROVIDER_MODE="${AI_PROVIDER_MODE:-unknown}"
CONNECTOR_CONFIG_MODE="${CONNECTOR_CONFIG_MODE:-unknown}"

# ── Create artifact directory tree ───────────────────────────────
mkdir -p "$RESULTS_DIR/scenarios"
mkdir -p "$RESULTS_DIR/logs"

K6_EXTRA_ARGS=""
if [[ "$QUICK_MODE" == "--quick" ]]; then
  echo "=== QUICK MODE: 30s durations for dry-run validation ==="
  K6_EXTRA_ARGS="--duration 30s"
fi

# Baseline scenarios in execution order.
# Multi-tenant is position 3 — if it fails, we stop.
BASELINE_SCENARIOS=(
  "api-crud"
  "governance-load"
  "multi-tenant-isolation"
  "connector-load"
  "intelligence-loop-stress"
)

SCENARIO_LABELS=(
  "API CRUD (core latency)"
  "Governance / Policy Evaluation"
  "Multi-Tenant Isolation (CRITICAL)"
  "Connector Load (external integrations)"
  "Intelligence Loop Stress (AI workflow)"
)

PASS_COUNT=0
FAIL_COUNT=0
RESULTS_SUMMARY=()

echo "=============================================="
echo " ArchonAI Staging Baseline"
echo " Target:      $BASE_URL"
echo " Environment: $ENVIRONMENT_NAME"
echo " Commit:      $GIT_SHA"
echo " Branch:      $GIT_BRANCH"
echo " Image Tag:   $DEPLOY_IMAGE_TAG"
echo " AI Provider: $AI_PROVIDER_MODE"
echo " Connectors:  $CONNECTOR_CONFIG_MODE"
echo " Timestamp:   $TIMESTAMP"
echo " Output:      $RESULTS_DIR"
echo " Scenarios:   ${#BASELINE_SCENARIOS[@]}"
echo "=============================================="

# Preflight: check gateway health
echo ""
echo "--- Preflight: checking gateway health ---"
if ! curl -sf "${BASE_URL}/healthz/live" > /dev/null 2>&1; then
  echo "FATAL: Gateway not reachable at ${BASE_URL}/healthz/live"
  echo "Ensure staging is running and BASE_URL is correct."
  exit 1
fi
echo "Gateway is healthy."

for i in "${!BASELINE_SCENARIOS[@]}"; do
  scenario="${BASELINE_SCENARIOS[$i]}"
  label="${SCENARIO_LABELS[$i]}"
  SCENARIO_FILE="$SCRIPT_DIR/scenarios/${scenario}.js"

  if [[ ! -f "$SCENARIO_FILE" ]]; then
    echo "SKIP: $scenario (file not found at $SCENARIO_FILE)"
    continue
  fi

  echo ""
  echo "=============================================="
  echo " [$((i+1))/${#BASELINE_SCENARIOS[@]}] $label"
  echo "=============================================="

  JSON_REPORT="$RESULTS_DIR/scenarios/${scenario}.json"
  SUMMARY_EXPORT="$RESULTS_DIR/scenarios/${scenario}-summary.json"
  LOG_FILE="$RESULTS_DIR/logs/${scenario}.log"

  set +e
  k6 run \
    --out "json=$JSON_REPORT" \
    --summary-export "$SUMMARY_EXPORT" \
    --env "BASE_URL=$BASE_URL" \
    --env "REPORT_DIR=$RESULTS_DIR" \
    --env "TENANT_COUNT=10" \
    $K6_EXTRA_ARGS \
    "$SCENARIO_FILE" \
    2>&1 | tee "$LOG_FILE"

  EXIT_CODE=${PIPESTATUS[0]}
  set -e

  if [[ $EXIT_CODE -eq 0 ]]; then
    PASS_COUNT=$((PASS_COUNT + 1))
    RESULTS_SUMMARY+=("PASS: $scenario")
    echo "--- $scenario: PASSED ---"
  else
    FAIL_COUNT=$((FAIL_COUNT + 1))
    RESULTS_SUMMARY+=("FAIL: $scenario (exit code: $EXIT_CODE)")
    echo "--- $scenario: FAILED (exit code: $EXIT_CODE) ---"

    # Multi-tenant isolation failure is a P0 — stop everything
    if [[ "$scenario" == "multi-tenant-isolation" ]]; then
      echo ""
      echo "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!"
      echo " CRITICAL: Multi-tenant isolation FAILED"
      echo " Cross-tenant data leakage may be present."
      echo " Stopping all further testing."
      echo " ACTION: File a P0 security bug immediately."
      echo "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!"
      echo ""
      break
    fi
  fi
done

echo ""
echo "=============================================="
echo " STAGING BASELINE RESULTS"
echo "=============================================="
echo " Passed: $PASS_COUNT"
echo " Failed: $FAIL_COUNT"
echo " Total:  $((PASS_COUNT + FAIL_COUNT))"
echo "----------------------------------------------"
for result in "${RESULTS_SUMMARY[@]}"; do
  echo "  $result"
done
echo "=============================================="
echo " Reports: $RESULTS_DIR"
echo "=============================================="

# ── Generate consolidated summary JSON with full metadata ────────
cat > "$RESULTS_DIR/baseline-summary.json" <<JSONEOF
{
  "type": "staging-baseline",
  "timestamp": "$TIMESTAMP",
  "metadata": {
    "gitCommitSha": "$GIT_SHA",
    "gitBranch": "$GIT_BRANCH",
    "environment": "$ENVIRONMENT_NAME",
    "deployImageTag": "$DEPLOY_IMAGE_TAG",
    "migrationVersion": "$MIGRATION_VERSION",
    "aiProviderMode": "$AI_PROVIDER_MODE",
    "connectorConfigMode": "$CONNECTOR_CONFIG_MODE",
    "baseUrl": "$BASE_URL",
    "k6Version": "$K6_VER",
    "quickMode": $(if [[ "$QUICK_MODE" == "--quick" ]]; then echo "true"; else echo "false"; fi)
  },
  "passed": $PASS_COUNT,
  "failed": $FAIL_COUNT,
  "total": $((PASS_COUNT + FAIL_COUNT)),
  "scenarios": [
$(printf '    "%s",\n' "${RESULTS_SUMMARY[@]}" | sed '$ s/,$//')
  ],
  "artifacts": {
    "scenarioDir": "scenarios/",
    "logsDir": "logs/",
    "files": [
$(find "$RESULTS_DIR" -type f -name "*.json" -o -name "*.log" | sort | sed "s|$RESULTS_DIR/||" | while read -r f; do printf '      "%s",\n' "$f"; done | sed '$ s/,$//')
    ]
  }
}
JSONEOF

echo ""
echo "Consolidated summary: $RESULTS_DIR/baseline-summary.json"

# ── Extract key metrics from summary files if jq is available ────
if command -v jq &> /dev/null; then
  echo ""
  echo "=============================================="
  echo " KEY METRICS (from summary exports)"
  echo "=============================================="
  for scenario in "${BASELINE_SCENARIOS[@]}"; do
    SUMMARY_FILE="$RESULTS_DIR/scenarios/${scenario}-summary.json"
    if [[ -f "$SUMMARY_FILE" ]]; then
      echo ""
      echo "--- $scenario ---"
      jq -r '{
        p50: (.metrics.http_req_duration.values["p(50)"] // "N/A"),
        p95: (.metrics.http_req_duration.values["p(95)"] // "N/A"),
        p99: (.metrics.http_req_duration.values["p(99)"] // "N/A"),
        error_rate: (.metrics.http_req_failed.values.rate // "N/A"),
        total_requests: (.metrics.http_reqs.values.count // "N/A"),
        throughput_rps: (.metrics.http_reqs.values.rate // "N/A")
      }' "$SUMMARY_FILE" 2>/dev/null || echo "  (could not parse summary)"
    fi
  done

  # Special check for cross-tenant violations
  MT_SUMMARY="$RESULTS_DIR/scenarios/multi-tenant-isolation-summary.json"
  if [[ -f "$MT_SUMMARY" ]]; then
    echo ""
    echo "--- TENANT ISOLATION ---"
    VIOLATIONS=$(jq -r '.metrics.archon_cross_tenant_violations.values.count // 0' "$MT_SUMMARY" 2>/dev/null || echo "unknown")
    echo "  Cross-tenant violations: $VIOLATIONS"
    if [[ "$VIOLATIONS" != "0" && "$VIOLATIONS" != "unknown" ]]; then
      echo "  STATUS: SECURITY VIOLATION DETECTED"
    else
      echo "  STATUS: ISOLATION VERIFIED"
    fi
  fi
fi

# ── Print artifact tree ──────────────────────────────────────────
echo ""
echo "=============================================="
echo " ARTIFACT TREE"
echo "=============================================="
if command -v find &> /dev/null; then
  find "$RESULTS_DIR" -type f | sort | sed "s|$RESULTS_DIR/|  |"
fi
echo "=============================================="

if [[ $FAIL_COUNT -gt 0 ]]; then
  exit 1
fi

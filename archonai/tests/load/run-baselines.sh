#!/usr/bin/env bash
# ArchonAI Full Baseline Runner
# Runs ALL 12 k6 load test scenarios sequentially with 30s cooldown between each.
# Captures JSON output to docs/performance/results/YYYY-MM-DD/ and generates
# a consolidated summary report.
#
# Required environment variables:
#   BASE_URL        - Target API endpoint (e.g. http://staging:8080)
#   ADMIN_EMAIL     - Admin user email for authenticated scenarios
#   ADMIN_PASSWORD  - Admin user password
#
# Optional environment variables:
#   REPORT_DIR      - Override output directory (default: docs/performance/results/YYYY-MM-DD)
#   COOLDOWN_SECS   - Seconds to wait between scenarios (default: 30)
#   QUICK_MODE      - Set to "true" to override durations to 30s (for dry-run validation)
#
# Usage:
#   BASE_URL=http://staging:8080 \
#   ADMIN_EMAIL=admin@archonai.dev \
#   ADMIN_PASSWORD=secret \
#   ./run-baselines.sh
#
#   # Dry-run with reduced durations:
#   BASE_URL=http://localhost:8080 ADMIN_EMAIL=a@b.c ADMIN_PASSWORD=pw ./run-baselines.sh --quick

set -euo pipefail

# ── Configuration ────────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
DATE_STAMP=$(date +%Y-%m-%d)
TIMESTAMP=$(date +%Y%m%d-%H%M%S)

BASE_URL="${BASE_URL:?BASE_URL is required (e.g. http://staging:8080)}"
ADMIN_EMAIL="${ADMIN_EMAIL:?ADMIN_EMAIL is required}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:?ADMIN_PASSWORD is required}"

REPORT_DIR="${REPORT_DIR:-$PROJECT_ROOT/docs/performance/results/$DATE_STAMP}"
COOLDOWN_SECS="${COOLDOWN_SECS:-30}"
QUICK_MODE="${1:-${QUICK_MODE:-}}"

# ── Capture run identity metadata ───────────────────────────────
GIT_SHA=$(git rev-parse HEAD 2>/dev/null || echo "unknown")
GIT_BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "unknown")
K6_VER=$(k6 version 2>/dev/null || echo "unknown")

# ── Create artifact directory tree ───────────────────────────────
mkdir -p "$REPORT_DIR/scenarios"
mkdir -p "$REPORT_DIR/logs"

# ── Quick mode ───────────────────────────────────────────────────
K6_EXTRA_ARGS=""
if [[ "$QUICK_MODE" == "--quick" || "$QUICK_MODE" == "true" ]]; then
  echo "=== QUICK MODE: 30s durations for dry-run validation ==="
  K6_EXTRA_ARGS="--duration 30s"
fi

# ── All 12 scenarios in execution order ──────────────────────────
SCENARIOS=(
  "auth-flow"
  "api-crud"
  "gateway-throughput"
  "multi-tenant-isolation"
  "connector-load"
  "intelligence-loop-stress"
  "hero-workflow-composition"
  "proof-analytics-volume"
  "soak-test"
  "governance-load"
  "agent-execution-stress"
  "connector-resilience"
)

declare -A SCENARIO_LABELS=(
  ["auth-flow"]="1/12  Authentication Flow"
  ["api-crud"]="2/12  API CRUD Operations"
  ["gateway-throughput"]="3/12  Gateway Throughput & Rate Limiting"
  ["multi-tenant-isolation"]="4/12  Multi-Tenant Isolation (CRITICAL)"
  ["connector-load"]="5/12  Connector Load & Retry"
  ["intelligence-loop-stress"]="6/12  Intelligence Loop Stress"
  ["hero-workflow-composition"]="7/12  Hero Workflow Composition (7-Service)"
  ["proof-analytics-volume"]="8/12  Proof Analytics Volume"
  ["soak-test"]="9/12  Soak / Stability Test"
  ["governance-load"]="10/12 Governance & Policy Evaluation"
  ["agent-execution-stress"]="11/12 Agent Execution Stress"
  ["connector-resilience"]="12/12 Connector Resilience"
)

# ── Tracking ─────────────────────────────────────────────────────
PASS_COUNT=0
FAIL_COUNT=0
declare -A SCENARIO_STATUS
declare -A SCENARIO_EXIT_CODES
RESULTS_SUMMARY=()

# ── Banner ───────────────────────────────────────────────────────
echo "=============================================="
echo " ArchonAI Full Baseline Capture"
echo "=============================================="
echo " Target:    $BASE_URL"
echo " Commit:    $GIT_SHA"
echo " Branch:    $GIT_BRANCH"
echo " k6:        $K6_VER"
echo " Timestamp: $TIMESTAMP"
echo " Output:    $REPORT_DIR"
echo " Scenarios: ${#SCENARIOS[@]}"
echo " Cooldown:  ${COOLDOWN_SECS}s between scenarios"
echo "=============================================="

# ── Preflight: check gateway health ─────────────────────────────
echo ""
echo "--- Preflight: checking gateway health ---"
if ! curl -sf "${BASE_URL}/healthz/live" > /dev/null 2>&1; then
  echo "FATAL: Gateway not reachable at ${BASE_URL}/healthz/live"
  echo "Ensure the target environment is running and BASE_URL is correct."
  exit 1
fi
echo "Gateway is healthy."

# ── Run each scenario ────────────────────────────────────────────
for i in "${!SCENARIOS[@]}"; do
  scenario="${SCENARIOS[$i]}"
  label="${SCENARIO_LABELS[$scenario]}"
  SCENARIO_FILE="$SCRIPT_DIR/scenarios/${scenario}.js"

  if [[ ! -f "$SCENARIO_FILE" ]]; then
    echo ""
    echo "SKIP: $scenario (file not found at $SCENARIO_FILE)"
    continue
  fi

  echo ""
  echo "=============================================="
  echo " [$label]"
  echo "=============================================="

  JSON_REPORT="$REPORT_DIR/scenarios/${scenario}.json"
  SUMMARY_EXPORT="$REPORT_DIR/scenarios/${scenario}-summary.json"
  LOG_FILE="$REPORT_DIR/logs/${scenario}.log"

  set +e
  k6 run \
    --out "json=$JSON_REPORT" \
    --summary-export "$SUMMARY_EXPORT" \
    --env "BASE_URL=$BASE_URL" \
    --env "ADMIN_EMAIL=$ADMIN_EMAIL" \
    --env "ADMIN_PASSWORD=$ADMIN_PASSWORD" \
    --env "REPORT_DIR=$REPORT_DIR" \
    $K6_EXTRA_ARGS \
    "$SCENARIO_FILE" \
    2>&1 | tee "$LOG_FILE"

  EXIT_CODE=${PIPESTATUS[0]}
  set -e

  SCENARIO_EXIT_CODES[$scenario]=$EXIT_CODE

  if [[ $EXIT_CODE -eq 0 ]]; then
    PASS_COUNT=$((PASS_COUNT + 1))
    SCENARIO_STATUS[$scenario]="PASS"
    RESULTS_SUMMARY+=("PASS: $scenario")
    echo "--- $scenario: PASSED ---"
  else
    FAIL_COUNT=$((FAIL_COUNT + 1))
    SCENARIO_STATUS[$scenario]="FAIL"
    RESULTS_SUMMARY+=("FAIL: $scenario (exit code: $EXIT_CODE)")
    echo "--- $scenario: FAILED (exit code: $EXIT_CODE) ---"

    # Fail fast: stop immediately on any failure
    echo ""
    echo "!!! FAIL FAST: Scenario '$scenario' exited with code $EXIT_CODE."
    echo "!!! Stopping remaining scenarios. Fix the failure and re-run."
    echo ""
    break
  fi

  # Cooldown between scenarios (skip after the last one)
  if [[ $i -lt $((${#SCENARIOS[@]} - 1)) ]]; then
    echo "--- Cooldown: ${COOLDOWN_SECS}s ---"
    sleep "$COOLDOWN_SECS"
  fi
done

# ── Threshold Pass/Fail Report ───────────────────────────────────
echo ""
echo "=============================================="
echo " THRESHOLD PASS/FAIL REPORT"
echo "=============================================="

if command -v jq &> /dev/null; then
  for scenario in "${SCENARIOS[@]}"; do
    SUMMARY_FILE="$REPORT_DIR/scenarios/${scenario}-summary.json"
    if [[ -f "$SUMMARY_FILE" ]]; then
      echo ""
      echo "--- $scenario ---"
      # Extract threshold results from k6 summary export
      jq -r '
        if .metrics then
          .metrics | to_entries[] |
          select(.value.thresholds) |
          .value.thresholds | to_entries[] |
          "  \(.key): \(if .value.ok then "PASS" else "FAIL" end)"
        else
          "  (no threshold data)"
        end
      ' "$SUMMARY_FILE" 2>/dev/null || echo "  (could not parse thresholds)"
    fi
  done
else
  echo "  (jq not available — install jq to see per-threshold results)"
fi

# ── Key Metrics Summary ─────────────────────────────────────────
if command -v jq &> /dev/null; then
  echo ""
  echo "=============================================="
  echo " KEY METRICS (from summary exports)"
  echo "=============================================="
  for scenario in "${SCENARIOS[@]}"; do
    SUMMARY_FILE="$REPORT_DIR/scenarios/${scenario}-summary.json"
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
fi

# ── Overall Summary ─────────────────────────────────────────────
echo ""
echo "=============================================="
echo " BASELINE CAPTURE RESULTS"
echo "=============================================="
echo " Passed: $PASS_COUNT"
echo " Failed: $FAIL_COUNT"
echo " Total:  $((PASS_COUNT + FAIL_COUNT)) / ${#SCENARIOS[@]}"
echo "----------------------------------------------"
for result in "${RESULTS_SUMMARY[@]}"; do
  echo "  $result"
done
echo "=============================================="
echo " Reports: $REPORT_DIR"
echo "=============================================="

# ── Generate consolidated summary JSON ───────────────────────────
cat > "$REPORT_DIR/baseline-summary.json" <<JSONEOF
{
  "type": "full-baseline",
  "timestamp": "$TIMESTAMP",
  "date": "$DATE_STAMP",
  "metadata": {
    "gitCommitSha": "$GIT_SHA",
    "gitBranch": "$GIT_BRANCH",
    "baseUrl": "$BASE_URL",
    "k6Version": "$K6_VER",
    "cooldownSeconds": $COOLDOWN_SECS,
    "quickMode": $(if [[ "$QUICK_MODE" == "--quick" || "$QUICK_MODE" == "true" ]]; then echo "true"; else echo "false"; fi)
  },
  "passed": $PASS_COUNT,
  "failed": $FAIL_COUNT,
  "total": $((PASS_COUNT + FAIL_COUNT)),
  "totalExpected": ${#SCENARIOS[@]},
  "scenarios": [
$(printf '    "%s",\n' "${RESULTS_SUMMARY[@]}" | sed '$ s/,$//')
  ]
}
JSONEOF

echo ""
echo "Consolidated summary: $REPORT_DIR/baseline-summary.json"

# ── Artifact tree ────────────────────────────────────────────────
echo ""
echo "=============================================="
echo " ARTIFACT TREE"
echo "=============================================="
if command -v find &> /dev/null; then
  find "$REPORT_DIR" -type f | sort | sed "s|$REPORT_DIR/|  |"
fi
echo "=============================================="

# Exit with failure if any scenario failed
if [[ $FAIL_COUNT -gt 0 ]]; then
  exit 1
fi

echo ""
echo "All 12 scenarios passed. Baseline capture complete."
echo "Next step: Copy measured values into docs/performance/staging-baseline-results.md"

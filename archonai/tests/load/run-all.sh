#!/usr/bin/env bash
# ArchonAI k6 Load Test Runner
# Runs all load test scenarios sequentially with HTML+JSON reporting.
#
# Usage:
#   ./run-all.sh                    # Run all scenarios
#   ./run-all.sh auth-flow          # Run single scenario
#   ./run-all.sh --quick            # Run with reduced duration
#   BASE_URL=http://host:8080 ./run-all.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RESULTS_DIR="${REPORT_DIR:-$SCRIPT_DIR/results}"
BASE_URL="${BASE_URL:-http://localhost:8080}"
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
QUICK_MODE="${1:-}"

mkdir -p "$RESULTS_DIR"

SCENARIOS=(
  "auth-flow"
  "api-crud"
  "gateway-throughput"
  "multi-tenant-isolation"
  "connector-load"
  "intelligence-loop-stress"
  "hero-workflow-composition"
  "proof-analytics-volume"
  "governance-load"
  "agent-execution-stress"
  "connector-resilience"
  "soak-test"
)

# If a specific scenario is requested (and it's not a flag)
if [[ -n "$QUICK_MODE" && "$QUICK_MODE" != "--quick" ]]; then
  SCENARIOS=("$QUICK_MODE")
  QUICK_MODE=""
fi

# Quick mode overrides for CI preview runs
K6_EXTRA_ARGS=""
if [[ "$QUICK_MODE" == "--quick" ]]; then
  echo "=== QUICK MODE: Reduced durations for CI preview ==="
  K6_EXTRA_ARGS="--duration 30s"
fi

PASS_COUNT=0
FAIL_COUNT=0
RESULTS_SUMMARY=()

echo "=============================================="
echo " ArchonAI k6 Load Test Suite"
echo " Target: $BASE_URL"
echo " Timestamp: $TIMESTAMP"
echo " Scenarios: ${#SCENARIOS[@]}"
echo "=============================================="

# Section labels for clear output grouping
declare -A SCENARIO_SECTIONS=(
  ["auth-flow"]="Authentication"
  ["api-crud"]="API CRUD"
  ["gateway-throughput"]="Gateway & Rate Limiting"
  ["multi-tenant-isolation"]="Multi-Tenant Isolation"
  ["connector-load"]="Connector Load"
  ["intelligence-loop-stress"]="Intelligence Loop"
  ["hero-workflow-composition"]="Hero Workflow"
  ["proof-analytics-volume"]="Proof Analytics"
  ["governance-load"]="Governance & Approvals"
  ["agent-execution-stress"]="Agent Execution Stress"
  ["connector-resilience"]="Connector Resilience"
  ["soak-test"]="Soak / Stability"
)

PREV_SECTION=""

for scenario in "${SCENARIOS[@]}"; do
  SCENARIO_FILE="$SCRIPT_DIR/scenarios/${scenario}.js"

  if [[ ! -f "$SCENARIO_FILE" ]]; then
    echo "SKIP: $scenario (file not found)"
    continue
  fi

  # Print section header when entering a new group
  SECTION="${SCENARIO_SECTIONS[$scenario]:-Other}"
  if [[ "$SECTION" != "$PREV_SECTION" ]]; then
    echo ""
    echo "=============================================="
    echo " SECTION: $SECTION"
    echo "=============================================="
    PREV_SECTION="$SECTION"
  fi

  echo ""
  echo "--- Running: $scenario ---"

  JSON_REPORT="$RESULTS_DIR/${scenario}-${TIMESTAMP}.json"
  SUMMARY_EXPORT="$RESULTS_DIR/${scenario}-${TIMESTAMP}-summary.json"

  # Run k6 with JSON output and summary export
  set +e
  k6 run \
    --out "json=$JSON_REPORT" \
    --summary-export "$SUMMARY_EXPORT" \
    --env "BASE_URL=$BASE_URL" \
    --env "REPORT_DIR=$RESULTS_DIR" \
    $K6_EXTRA_ARGS \
    "$SCENARIO_FILE" \
    2>&1 | tee "$RESULTS_DIR/${scenario}-${TIMESTAMP}.log"

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
  fi
done

echo ""
echo "=============================================="
echo " LOAD TEST RESULTS SUMMARY"
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

# Generate consolidated JSON summary for CI consumption
cat > "$RESULTS_DIR/summary-${TIMESTAMP}.json" <<JSONEOF
{
  "timestamp": "$TIMESTAMP",
  "baseUrl": "$BASE_URL",
  "passed": $PASS_COUNT,
  "failed": $FAIL_COUNT,
  "total": $((PASS_COUNT + FAIL_COUNT)),
  "scenarios": [
$(printf '    "%s",\n' "${RESULTS_SUMMARY[@]}" | sed '$ s/,$//')
  ]
}
JSONEOF

# Exit with failure if any scenario failed
if [[ $FAIL_COUNT -gt 0 ]]; then
  exit 1
fi

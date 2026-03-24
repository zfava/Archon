#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════
# ArchonAI Demo — Assertion Library
# Provides test-style assertions for curl-based API demo phases.
# ═══════════════════════════════════════════════════════════════
set -euo pipefail

# Counters (exported so phases share them)
export ASSERT_PASS=${ASSERT_PASS:-0}
export ASSERT_FAIL=${ASSERT_FAIL:-0}
export ASSERT_ERRORS=""

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

# ───────────────────────────────────────────────────────────────
# Internal: record pass/fail
# ───────────────────────────────────────────────────────────────
_pass() {
  ASSERT_PASS=$((ASSERT_PASS + 1))
  echo -e "  ${GREEN}  PASS${NC} $1"
}

_fail() {
  ASSERT_FAIL=$((ASSERT_FAIL + 1))
  local msg="$1"
  ASSERT_ERRORS="${ASSERT_ERRORS}\n    - ${msg}"
  echo -e "  ${RED}  FAIL${NC} $msg"
}

# ───────────────────────────────────────────────────────────────
# assert_status RESPONSE_FILE EXPECTED_CODE LABEL
#   Verify the HTTP status code of a curl response.
# ───────────────────────────────────────────────────────────────
assert_status() {
  local response="$1" expected="$2" label="${3:-HTTP status}"
  local actual
  actual=$(echo "$response" | tail -1)
  if [[ "$actual" == "$expected" ]]; then
    _pass "$label (HTTP $expected)"
  else
    _fail "$label — expected HTTP $expected, got HTTP $actual"
  fi
}

# ───────────────────────────────────────────────────────────────
# assert_status_code CODE EXPECTED LABEL
#   Compare a pre-extracted status code.
# ───────────────────────────────────────────────────────────────
assert_status_code() {
  local actual="$1" expected="$2" label="${3:-HTTP status}"
  if [[ "$actual" == "$expected" ]]; then
    _pass "$label (HTTP $expected)"
  else
    _fail "$label — expected HTTP $expected, got HTTP $actual"
  fi
}

# ───────────────────────────────────────────────────────────────
# assert_json_field JSON FIELD EXPECTED LABEL
#   Verify a top-level JSON field matches an expected value.
#   Uses jq. Strings are compared without quotes.
# ───────────────────────────────────────────────────────────────
assert_json_field() {
  local json="$1" field="$2" expected="$3" label="${4:-JSON field $field}"
  local actual
  actual=$(echo "$json" | jq -r "$field" 2>/dev/null || echo "__JQ_ERROR__")
  if [[ "$actual" == "__JQ_ERROR__" ]]; then
    _fail "$label — jq failed to parse field '$field'"
  elif [[ "$actual" == "$expected" ]]; then
    _pass "$label = $expected"
  else
    _fail "$label — expected '$expected', got '$actual'"
  fi
}

# ───────────────────────────────────────────────────────────────
# assert_json_field_exists JSON FIELD LABEL
#   Verify a JSON field exists and is not null.
# ───────────────────────────────────────────────────────────────
assert_json_field_exists() {
  local json="$1" field="$2" label="${3:-JSON field $field exists}"
  local actual
  actual=$(echo "$json" | jq -r "$field" 2>/dev/null || echo "null")
  if [[ "$actual" != "null" && "$actual" != "" ]]; then
    _pass "$label"
  else
    _fail "$label — field '$field' is null or missing"
  fi
}

# ───────────────────────────────────────────────────────────────
# assert_json_contains JSON FIELD SUBSTRING LABEL
#   Verify a JSON string field contains a substring.
# ───────────────────────────────────────────────────────────────
assert_json_contains() {
  local json="$1" field="$2" substring="$3" label="${4:-JSON $field contains '$substring'}"
  local actual
  actual=$(echo "$json" | jq -r "$field" 2>/dev/null || echo "")
  if [[ "$actual" == *"$substring"* ]]; then
    _pass "$label"
  else
    _fail "$label — '$field' does not contain '$substring' (got: '$actual')"
  fi
}

# ───────────────────────────────────────────────────────────────
# assert_json_count JSON JQ_EXPR EXPECTED LABEL
#   Verify the count of a jq array expression.
# ───────────────────────────────────────────────────────────────
assert_json_count() {
  local json="$1" expr="$2" expected="$3" label="${4:-count $expr}"
  local actual
  actual=$(echo "$json" | jq "$expr" 2>/dev/null || echo "-1")
  if [[ "$actual" == "$expected" ]]; then
    _pass "$label = $expected"
  else
    _fail "$label — expected $expected, got $actual"
  fi
}

# ───────────────────────────────────────────────────────────────
# assert_json_gte JSON JQ_EXPR MINIMUM LABEL
#   Verify a jq numeric expression is >= MINIMUM.
# ───────────────────────────────────────────────────────────────
assert_json_gte() {
  local json="$1" expr="$2" minimum="$3" label="${4:-$expr >= $minimum}"
  local actual
  actual=$(echo "$json" | jq "$expr" 2>/dev/null || echo "-1")
  if [[ "$actual" -ge "$minimum" ]]; then
    _pass "$label ($actual >= $minimum)"
  else
    _fail "$label — expected >= $minimum, got $actual"
  fi
}

# ───────────────────────────────────────────────────────────────
# assert_json_lt JSON JQ_EXPR MAX LABEL
#   Verify a jq numeric expression is < MAX.
# ───────────────────────────────────────────────────────────────
assert_json_lt() {
  local json="$1" expr="$2" max="$3" label="${4:-$expr < $max}"
  local actual
  actual=$(echo "$json" | jq "$expr" 2>/dev/null || echo "999999")
  if [[ "$actual" -lt "$max" ]]; then
    _pass "$label ($actual < $max)"
  else
    _fail "$label — expected < $max, got $actual"
  fi
}

# ───────────────────────────────────────────────────────────────
# assert_not_empty VALUE LABEL
#   Verify a value is not empty.
# ───────────────────────────────────────────────────────────────
assert_not_empty() {
  local value="$1" label="${2:-value not empty}"
  if [[ -n "$value" && "$value" != "null" ]]; then
    _pass "$label"
  else
    _fail "$label — value is empty or null"
  fi
}

# ───────────────────────────────────────────────────────────────
# assert_zero VALUE LABEL
#   Verify a numeric value is zero.
# ───────────────────────────────────────────────────────────────
assert_zero() {
  local value="$1" label="${2:-value is zero}"
  if [[ "$value" == "0" ]]; then
    _pass "$label"
  else
    _fail "$label — expected 0, got $value"
  fi
}

# ───────────────────────────────────────────────────────────────
# assert_equals ACTUAL EXPECTED LABEL
#   Generic equality assertion.
# ───────────────────────────────────────────────────────────────
assert_equals() {
  local actual="$1" expected="$2" label="${3:-equality check}"
  if [[ "$actual" == "$expected" ]]; then
    _pass "$label"
  else
    _fail "$label — expected '$expected', got '$actual'"
  fi
}

# ───────────────────────────────────────────────────────────────
# Phase banner and summary helpers
# ───────────────────────────────────────────────────────────────
PHASE_START_PASS=0
PHASE_START_FAIL=0

phase_start() {
  local num="$1" name="$2" story="$3"
  PHASE_START_PASS=$ASSERT_PASS
  PHASE_START_FAIL=$ASSERT_FAIL
  echo ""
  echo -e "${BOLD}${CYAN}═══════════════════════════════════════════════════════════${NC}"
  echo -e "${BOLD}  PHASE $num: $name${NC}"
  echo -e "${CYAN}  $story${NC}"
  echo -e "${BOLD}${CYAN}═══════════════════════════════════════════════════════════${NC}"
}

phase_end() {
  local num="$1" name="$2"
  local phase_pass=$((ASSERT_PASS - PHASE_START_PASS))
  local phase_fail=$((ASSERT_FAIL - PHASE_START_FAIL))
  local phase_total=$((phase_pass + phase_fail))
  if [[ "$phase_fail" -eq 0 ]]; then
    echo -e "${GREEN}${BOLD}  >>> Phase $num: $name — $phase_pass/$phase_total assertions passed${NC}"
    echo "PHASE_${num}_RESULT=PASS" >> "${DEMO_RESULTS_FILE:-/dev/null}"
  else
    echo -e "${RED}${BOLD}  >>> Phase $num: $name — FAILED ($phase_fail/$phase_total assertions failed)${NC}"
    echo "PHASE_${num}_RESULT=FAIL" >> "${DEMO_RESULTS_FILE:-/dev/null}"
  fi
  echo "PHASE_${num}_PASS=$phase_pass" >> "${DEMO_RESULTS_FILE:-/dev/null}"
  echo "PHASE_${num}_TOTAL=$phase_total" >> "${DEMO_RESULTS_FILE:-/dev/null}"
  echo "PHASE_${num}_NAME=$name" >> "${DEMO_RESULTS_FILE:-/dev/null}"
}

# ───────────────────────────────────────────────────────────────
# demo_summary — final summary printout
# ───────────────────────────────────────────────────────────────
demo_summary() {
  local total=$((ASSERT_PASS + ASSERT_FAIL))
  echo ""
  echo -e "${BOLD}${CYAN}═══════════════════════════════════════════════════════════${NC}"
  echo -e "${BOLD}  DEMO SUMMARY${NC}"
  echo -e "${BOLD}${CYAN}═══════════════════════════════════════════════════════════${NC}"
  echo ""

  # Read phase results
  local phases_passed=0
  local phases_total=0
  for i in $(seq 1 12); do
    local result_var="PHASE_${i}_RESULT"
    local result
    result=$(grep "^${result_var}=" "${DEMO_RESULTS_FILE:-/dev/null}" 2>/dev/null | cut -d= -f2 || echo "SKIP")
    if [[ "$result" == "PASS" ]]; then
      phases_passed=$((phases_passed + 1))
      phases_total=$((phases_total + 1))
    elif [[ "$result" == "FAIL" ]]; then
      phases_total=$((phases_total + 1))
    fi
  done

  if [[ "$ASSERT_FAIL" -eq 0 ]]; then
    echo -e "  ${GREEN}${BOLD}Demo complete: $phases_passed/$phases_total phases passed, $ASSERT_PASS/$total total assertions passed${NC}"
  else
    echo -e "  ${RED}${BOLD}Demo complete: $phases_passed/$phases_total phases passed, $ASSERT_PASS/$total total assertions passed${NC}"
    echo -e "  ${RED}Failures:${ASSERT_ERRORS}${NC}"
  fi
  echo ""
  echo "  Total assertions: $total"
  echo "  Passed: $ASSERT_PASS"
  echo "  Failed: $ASSERT_FAIL"
  echo ""

  # Write summary to results file
  echo "TOTAL_PASS=$ASSERT_PASS" >> "${DEMO_RESULTS_FILE:-/dev/null}"
  echo "TOTAL_FAIL=$ASSERT_FAIL" >> "${DEMO_RESULTS_FILE:-/dev/null}"
  echo "TOTAL_ASSERTIONS=$total" >> "${DEMO_RESULTS_FILE:-/dev/null}"
  echo "PHASES_PASSED=$phases_passed" >> "${DEMO_RESULTS_FILE:-/dev/null}"
  echo "PHASES_TOTAL=$phases_total" >> "${DEMO_RESULTS_FILE:-/dev/null}"

  if [[ "$ASSERT_FAIL" -eq 0 ]]; then
    return 0
  else
    return 1
  fi
}

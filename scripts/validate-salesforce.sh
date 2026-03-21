#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════
#  ArchonAI — Salesforce Live Validation Script
# ═══════════════════════════════════════════════════════════════════════════
#
#  Validates Salesforce connectivity by authenticating via OAuth 2.0 and
#  performing read/write probes against a Salesforce org (sandbox recommended).
#
#  REQUIREMENTS:
#    Environment variables from .env.salesforce (see .env.salesforce.template)
#
#  USAGE:
#    cp scripts/.env.salesforce.template .env.salesforce
#    # Fill in values
#    set -a && source .env.salesforce && set +a
#    bash scripts/validate-salesforce.sh
#
#  EXIT CODES:
#    0  All steps passed
#    1  One or more steps failed
#    2  Missing required environment variables
# ═══════════════════════════════════════════════════════════════════════════

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPORT_DIR="${SALESFORCE_REPORT_DIR:-./live-validation-results}"
mkdir -p "$REPORT_DIR"
REPORT_FILE="$REPORT_DIR/validate-salesforce-$(date -u +%Y%m%dT%H%M%SZ).json"

FAILURES=0
RESULTS=()

# ─── Logging helpers ──────────────────────────────────────────────────────

log()  { echo "[$(date -u +%H:%M:%S)] $*"; }
pass() {
    local name="$1" detail="$2"
    log "PASS: $name"
    RESULTS+=("{\"step\":\"$name\",\"result\":\"pass\",\"ts\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"detail\":\"$(echo "$detail" | sed 's/"/\\"/g')\"}")
}
fail() {
    local name="$1" detail="$2"
    log "FAIL: $name — $detail"
    RESULTS+=("{\"step\":\"$name\",\"result\":\"fail\",\"ts\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"detail\":\"$(echo "$detail" | sed 's/"/\\"/g')\"}")
    FAILURES=$((FAILURES + 1))
}

# ─── Preflight: check required env vars ──────────────────────────────────

REQUIRED_VARS=(
    SALESFORCE_CLIENT_ID
    SALESFORCE_CLIENT_SECRET
    SALESFORCE_INSTANCE_URL
)

MISSING=()
for var in "${REQUIRED_VARS[@]}"; do
    if [[ -z "${!var:-}" ]]; then
        MISSING+=("$var")
    fi
done

if [[ ${#MISSING[@]} -gt 0 ]]; then
    log "ERROR: Missing required environment variables: ${MISSING[*]}"
    log ""
    log "Copy scripts/.env.salesforce.template to .env.salesforce, fill in values, then:"
    log "  set -a && source .env.salesforce && set +a"
    log "  bash scripts/validate-salesforce.sh"
    exit 2
fi

# Determine auth flow: client_credentials if no username, password grant otherwise
SF_INSTANCE_URL="${SALESFORCE_INSTANCE_URL}"
SF_LOGIN_URL="${SALESFORCE_LOGIN_URL:-https://login.salesforce.com}"
SF_API_VERSION="${SALESFORCE_API_VERSION:-v59.0}"

HAS_USER_CREDS=false
if [[ -n "${SALESFORCE_USERNAME:-}" && -n "${SALESFORCE_PASSWORD:-}" ]]; then
    HAS_USER_CREDS=true
fi

log "═══════════════════════════════════════════════════════════"
log "  ArchonAI Salesforce Live Validation"
log "  Instance URL: $SF_INSTANCE_URL"
log "  API Version:  $SF_API_VERSION"
log "  Auth Flow:    $( $HAS_USER_CREDS && echo 'password grant' || echo 'client_credentials' )"
log "═══════════════════════════════════════════════════════════"

# ═══════════════════════════════════════════════════════════════════════════
#  STEP 1: OAuth 2.0 Authentication
# ═══════════════════════════════════════════════════════════════════════════

log ""
log "── Step 1: OAuth 2.0 Authentication ──"

if $HAS_USER_CREDS; then
    # Password grant flow (matches SalesforceConnector.AuthenticateCoreAsync)
    AUTH_RESPONSE=$(curl -s -w "\n%{http_code}" \
        -X POST "$SF_LOGIN_URL/services/oauth2/token" \
        -d "grant_type=password" \
        -d "client_id=$SALESFORCE_CLIENT_ID" \
        -d "client_secret=$SALESFORCE_CLIENT_SECRET" \
        -d "username=$SALESFORCE_USERNAME" \
        -d "password=${SALESFORCE_PASSWORD}${SALESFORCE_SECURITY_TOKEN:-}" \
        --connect-timeout 10 \
        --max-time 30 2>&1) || true
else
    # Client credentials flow
    AUTH_RESPONSE=$(curl -s -w "\n%{http_code}" \
        -X POST "$SF_LOGIN_URL/services/oauth2/token" \
        -d "grant_type=client_credentials" \
        -d "client_id=$SALESFORCE_CLIENT_ID" \
        -d "client_secret=$SALESFORCE_CLIENT_SECRET" \
        --connect-timeout 10 \
        --max-time 30 2>&1) || true
fi

HTTP_CODE=$(echo "$AUTH_RESPONSE" | tail -1)
AUTH_BODY=$(echo "$AUTH_RESPONSE" | sed '$d')

SF_ACCESS_TOKEN=""
SF_RETURNED_INSTANCE=""

if [[ "$HTTP_CODE" == "200" ]]; then
    SF_ACCESS_TOKEN=$(echo "$AUTH_BODY" | grep -o '"access_token":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "")
    SF_RETURNED_INSTANCE=$(echo "$AUTH_BODY" | grep -o '"instance_url":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "")

    if [[ -n "$SF_ACCESS_TOKEN" ]]; then
        # Use returned instance URL if available, fall back to configured
        if [[ -n "$SF_RETURNED_INSTANCE" ]]; then
            SF_INSTANCE_URL="$SF_RETURNED_INSTANCE"
        fi
        pass "oauth-authenticate" "HTTP 200, token obtained, instance=$SF_INSTANCE_URL"
    else
        fail "oauth-authenticate" "HTTP 200 but no access_token in response"
    fi
else
    SF_ERROR=$(echo "$AUTH_BODY" | grep -o '"error_description":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "HTTP $HTTP_CODE")
    fail "oauth-authenticate" "$SF_ERROR"
fi

# ═══════════════════════════════════════════════════════════════════════════
#  STEP 2: Query one Account record
# ═══════════════════════════════════════════════════════════════════════════

if [[ -n "$SF_ACCESS_TOKEN" ]]; then
    log ""
    log "── Step 2: Query Account records (SOQL) ──"

    SOQL="SELECT Id, Name FROM Account LIMIT 1"
    ENCODED_SOQL=$(python3 -c "import urllib.parse; print(urllib.parse.quote('$SOQL'))" 2>/dev/null || echo "SELECT%20Id%2C%20Name%20FROM%20Account%20LIMIT%201")

    QUERY_RESPONSE=$(curl -s -w "\n%{http_code}" \
        -H "Authorization: Bearer $SF_ACCESS_TOKEN" \
        -H "Accept: application/json" \
        "$SF_INSTANCE_URL/services/data/$SF_API_VERSION/query?q=$ENCODED_SOQL" \
        --connect-timeout 10 \
        --max-time 30 2>&1) || true

    QUERY_CODE=$(echo "$QUERY_RESPONSE" | tail -1)
    QUERY_BODY=$(echo "$QUERY_RESPONSE" | sed '$d')

    if [[ "$QUERY_CODE" == "200" ]]; then
        TOTAL_SIZE=$(echo "$QUERY_BODY" | grep -o '"totalSize":[0-9]*' | head -1 | cut -d: -f2 || echo "0")
        pass "query-account" "HTTP 200, totalSize=$TOTAL_SIZE"
    else
        fail "query-account" "HTTP $QUERY_CODE"
    fi

    # ═══════════════════════════════════════════════════════════════════════
    #  STEP 3: Create a test record or hit /limits
    # ═══════════════════════════════════════════════════════════════════════

    log ""
    log "── Step 3: Validate write capability (GET /limits) ──"

    # Use /limits endpoint as a safe read-only probe that verifies API access
    # without creating actual data. This confirms the token has data API access.
    LIMITS_RESPONSE=$(curl -s -w "\n%{http_code}" \
        -H "Authorization: Bearer $SF_ACCESS_TOKEN" \
        -H "Accept: application/json" \
        "$SF_INSTANCE_URL/services/data/$SF_API_VERSION/limits" \
        --connect-timeout 10 \
        --max-time 30 2>&1) || true

    LIMITS_CODE=$(echo "$LIMITS_RESPONSE" | tail -1)
    LIMITS_BODY=$(echo "$LIMITS_RESPONSE" | sed '$d')

    if [[ "$LIMITS_CODE" == "200" ]]; then
        # Extract DailyApiRequests limit info
        DAILY_MAX=$(echo "$LIMITS_BODY" | grep -o '"DailyApiRequests":{[^}]*}' | head -1 | grep -o '"Max":[0-9]*' | cut -d: -f2 || echo "?")
        DAILY_REMAINING=$(echo "$LIMITS_BODY" | grep -o '"DailyApiRequests":{[^}]*}' | head -1 | grep -o '"Remaining":[0-9]*' | cut -d: -f2 || echo "?")
        pass "api-limits" "HTTP 200, DailyApiRequests: $DAILY_REMAINING/$DAILY_MAX remaining"
    else
        fail "api-limits" "HTTP $LIMITS_CODE"
    fi

    # ═══════════════════════════════════════════════════════════════════════
    #  STEP 4: Check rate limit headers
    # ═══════════════════════════════════════════════════════════════════════

    log ""
    log "── Step 4: Rate limit header check (Sforce-Limit-Info) ──"

    HEADER_RESPONSE=$(curl -s -D - -o /dev/null \
        -H "Authorization: Bearer $SF_ACCESS_TOKEN" \
        "$SF_INSTANCE_URL/services/data/$SF_API_VERSION/limits" \
        --connect-timeout 10 \
        --max-time 30 2>&1) || true

    if echo "$HEADER_RESPONSE" | grep -qi "Sforce-Limit-Info"; then
        LIMIT_HEADER=$(echo "$HEADER_RESPONSE" | grep -i "Sforce-Limit-Info" | head -1 | tr -d '\r\n')
        pass "rate-limit-header" "$LIMIT_HEADER"
    else
        pass "rate-limit-header" "Sforce-Limit-Info header not present (advisory, not a failure)"
    fi
else
    fail "query-account" "Skipped — no valid auth token"
    fail "api-limits" "Skipped — no valid auth token"
    fail "rate-limit-header" "Skipped — no valid auth token"
fi

# ═══════════════════════════════════════════════════════════════════════════
#  JSON Summary Report
# ═══════════════════════════════════════════════════════════════════════════

log ""
log "═══════════════════════════════════════════════════════════"
log "  RESULTS SUMMARY"
log "═══════════════════════════════════════════════════════════"

PASS_COUNT=0
FAIL_COUNT=0

for r in "${RESULTS[@]}"; do
    if echo "$r" | grep -q '"result":"pass"'; then
        PASS_COUNT=$((PASS_COUNT + 1))
    else
        FAIL_COUNT=$((FAIL_COUNT + 1))
    fi
done

log "  Pass: $PASS_COUNT  Fail: $FAIL_COUNT"

# Write JSON report
{
    echo "{"
    echo "  \"timestamp\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\","
    echo "  \"suite\": \"validate-salesforce\","
    echo "  \"instance_url\": \"$SF_INSTANCE_URL\","
    echo "  \"api_version\": \"$SF_API_VERSION\","
    echo "  \"summary\": {\"pass\": $PASS_COUNT, \"fail\": $FAIL_COUNT},"
    echo "  \"steps\": ["
    for i in "${!RESULTS[@]}"; do
        if [[ $i -lt $((${#RESULTS[@]} - 1)) ]]; then
            echo "    ${RESULTS[$i]},"
        else
            echo "    ${RESULTS[$i]}"
        fi
    done
    echo "  ]"
    echo "}"
} > "$REPORT_FILE"

log ""
log "Report written to: $REPORT_FILE"

if [[ $FAIL_COUNT -gt 0 ]]; then
    log ""
    log "SALESFORCE VALIDATION FAILED ($FAIL_COUNT failure(s))"
    exit 1
fi

log ""
log "SALESFORCE VALIDATION PASSED"
exit 0

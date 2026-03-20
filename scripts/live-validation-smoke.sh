#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════
#  ArchonAI Live Validation Smoke Tests
# ═══════════════════════════════════════════════════════════════════════════
#
#  Validates ONE identity path (OIDC → Okta/Entra/Auth0) and ONE connector
#  path (Salesforce OAuth2) against real external systems.
#
#  REQUIREMENTS:
#    All credentials via environment variables — never hardcoded.
#    See docs/testing/live-validation-runbook.md for full setup.
#
#  USAGE:
#    export SALESFORCE_CLIENT_ID=... SALESFORCE_CLIENT_SECRET=... (etc)
#    export OIDC_AUTHORITY=... OIDC_CLIENT_ID=... (etc)
#    bash scripts/live-validation-smoke.sh
#
#  EXIT CODES:
#    0  All probes passed
#    1  One or more probes failed
#    2  Missing required environment variables
# ═══════════════════════════════════════════════════════════════════════════

set -euo pipefail

REPORT_DIR="${LIVE_VALIDATION_REPORT_DIR:-./live-validation-results}"
mkdir -p "$REPORT_DIR"
REPORT_FILE="$REPORT_DIR/live-validation-$(date -u +%Y%m%dT%H%M%SZ).json"
FAILURES=0
RESULTS=()

log()    { echo "[$(date -u +%H:%M:%S)] $*"; }
pass()   { log "✓ PASS: $1"; RESULTS+=("{\"probe\":\"$1\",\"result\":\"pass\",\"ts\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"detail\":\"$2\"}"); }
fail()   { log "✗ FAIL: $1 — $2"; RESULTS+=("{\"probe\":\"$1\",\"result\":\"fail\",\"ts\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"detail\":\"$2\"}"); FAILURES=$((FAILURES + 1)); }
skip()   { log "⊘ SKIP: $1 — $2"; RESULTS+=("{\"probe\":\"$1\",\"result\":\"skip\",\"ts\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"detail\":\"$2\"}"); }

# ─── Preflight: check required env vars ────────────────────────────────────

check_env_group() {
    local group_name="$1"; shift
    local missing=()
    for var in "$@"; do
        if [[ -z "${!var:-}" ]]; then
            missing+=("$var")
        fi
    done
    if [[ ${#missing[@]} -gt 0 ]]; then
        echo "$group_name:MISSING:${missing[*]}"
    else
        echo "$group_name:OK"
    fi
}

SF_CHECK=$(check_env_group "Salesforce" \
    SALESFORCE_CLIENT_ID SALESFORCE_CLIENT_SECRET \
    SALESFORCE_USERNAME SALESFORCE_PASSWORD SALESFORCE_SECURITY_TOKEN)

OIDC_CHECK=$(check_env_group "OIDC" \
    OIDC_AUTHORITY OIDC_CLIENT_ID)

SF_READY=false
OIDC_READY=false
[[ "$SF_CHECK" == *":OK" ]] && SF_READY=true
[[ "$OIDC_CHECK" == *":OK" ]] && OIDC_READY=true

log "═══════════════════════════════════════════════════════════"
log "  ArchonAI Live Validation Smoke Tests"
log "  Salesforce credentials: $( $SF_READY && echo 'PRESENT' || echo 'MISSING' )"
log "  OIDC credentials:       $( $OIDC_READY && echo 'PRESENT' || echo 'MISSING' )"
log "═══════════════════════════════════════════════════════════"

if ! $SF_READY && ! $OIDC_READY; then
    log ""
    log "ERROR: No credentials available for either path."
    log "Set environment variables per docs/testing/live-validation-runbook.md"
    log ""
    log "Salesforce: $SF_CHECK"
    log "OIDC:       $OIDC_CHECK"
    exit 2
fi

# ═══════════════════════════════════════════════════════════════════════════
#  PROBE 1: Salesforce OAuth2 Authentication
# ═══════════════════════════════════════════════════════════════════════════

if $SF_READY; then
    log ""
    log "── Probe 1: Salesforce OAuth2 Password Grant ──"

    SF_LOGIN_URL="${SALESFORCE_LOGIN_URL:-https://login.salesforce.com}"
    SF_API_VERSION="${SALESFORCE_API_VERSION:-v59.0}"

    SF_AUTH_RESPONSE=$(curl -s -w "\n%{http_code}" \
        -X POST "$SF_LOGIN_URL/services/oauth2/token" \
        -d "grant_type=password" \
        -d "client_id=$SALESFORCE_CLIENT_ID" \
        -d "client_secret=$SALESFORCE_CLIENT_SECRET" \
        -d "username=$SALESFORCE_USERNAME" \
        -d "password=${SALESFORCE_PASSWORD}${SALESFORCE_SECURITY_TOKEN}" \
        --connect-timeout 10 \
        --max-time 30 2>&1) || true

    SF_HTTP_CODE=$(echo "$SF_AUTH_RESPONSE" | tail -1)
    SF_AUTH_BODY=$(echo "$SF_AUTH_RESPONSE" | sed '$d')

    if [[ "$SF_HTTP_CODE" == "200" ]]; then
        SF_ACCESS_TOKEN=$(echo "$SF_AUTH_BODY" | grep -o '"access_token":"[^"]*"' | head -1 | cut -d'"' -f4)
        SF_INSTANCE_URL=$(echo "$SF_AUTH_BODY" | grep -o '"instance_url":"[^"]*"' | head -1 | cut -d'"' -f4)

        if [[ -n "$SF_ACCESS_TOKEN" && -n "$SF_INSTANCE_URL" ]]; then
            pass "sf-oauth2-auth" "HTTP 200, token obtained, instance=$SF_INSTANCE_URL"
        else
            fail "sf-oauth2-auth" "HTTP 200 but missing access_token or instance_url in response"
        fi
    else
        SF_ERROR=$(echo "$SF_AUTH_BODY" | grep -o '"error_description":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "HTTP $SF_HTTP_CODE")
        fail "sf-oauth2-auth" "$SF_ERROR"
    fi

    # ─── Probe 2: Salesforce SOQL Query (read-only) ─────────────────────────
    if [[ -n "${SF_ACCESS_TOKEN:-}" && -n "${SF_INSTANCE_URL:-}" ]]; then
        log ""
        log "── Probe 2: Salesforce SOQL Query (Account LIMIT 5) ──"

        SF_QUERY_URL="$SF_INSTANCE_URL/services/data/$SF_API_VERSION/query?q=$(python3 -c 'import urllib.parse; print(urllib.parse.quote("SELECT Id, Name FROM Account LIMIT 5"))' 2>/dev/null || echo 'SELECT%20Id%2C%20Name%20FROM%20Account%20LIMIT%205')"

        SF_QUERY_RESPONSE=$(curl -s -w "\n%{http_code}" \
            -H "Authorization: Bearer $SF_ACCESS_TOKEN" \
            -H "Content-Type: application/json" \
            "$SF_QUERY_URL" \
            --connect-timeout 10 \
            --max-time 30 2>&1) || true

        SF_QUERY_CODE=$(echo "$SF_QUERY_RESPONSE" | tail -1)
        SF_QUERY_BODY=$(echo "$SF_QUERY_RESPONSE" | sed '$d')

        if [[ "$SF_QUERY_CODE" == "200" ]]; then
            SF_TOTAL=$(echo "$SF_QUERY_BODY" | grep -o '"totalSize":[0-9]*' | head -1 | cut -d: -f2 || echo "?")
            pass "sf-soql-query" "HTTP 200, totalSize=$SF_TOTAL records"
        else
            fail "sf-soql-query" "HTTP $SF_QUERY_CODE"
        fi

        # ─── Probe 3: Salesforce Rate Limit Header Check ─────────────────────
        log ""
        log "── Probe 3: Salesforce Rate Limit Header (Sforce-Limit-Info) ──"

        SF_LIMITS_RESPONSE=$(curl -s -D - -o /dev/null \
            -H "Authorization: Bearer $SF_ACCESS_TOKEN" \
            "$SF_INSTANCE_URL/services/data/$SF_API_VERSION/limits" \
            --connect-timeout 10 \
            --max-time 30 2>&1) || true

        if echo "$SF_LIMITS_RESPONSE" | grep -qi "Sforce-Limit-Info"; then
            SF_LIMIT_HEADER=$(echo "$SF_LIMITS_RESPONSE" | grep -i "Sforce-Limit-Info" | head -1 | tr -d '\r')
            pass "sf-rate-limit-header" "$SF_LIMIT_HEADER"
        else
            # Rate limit header is advisory — not having it is not a failure
            skip "sf-rate-limit-header" "Sforce-Limit-Info header not present in /limits response"
        fi
    else
        skip "sf-soql-query" "Skipped — no valid auth token"
        skip "sf-rate-limit-header" "Skipped — no valid auth token"
    fi
else
    skip "sf-oauth2-auth" "Salesforce credentials not configured"
    skip "sf-soql-query" "Salesforce credentials not configured"
    skip "sf-rate-limit-header" "Salesforce credentials not configured"
fi

# ═══════════════════════════════════════════════════════════════════════════
#  PROBE 4: OIDC Discovery Document Fetch
# ═══════════════════════════════════════════════════════════════════════════

if $OIDC_READY; then
    log ""
    log "── Probe 4: OIDC Discovery Document ──"

    OIDC_DISCO_URL="${OIDC_AUTHORITY%/}/.well-known/openid-configuration"

    OIDC_DISCO_RESPONSE=$(curl -s -w "\n%{http_code}" \
        "$OIDC_DISCO_URL" \
        --connect-timeout 10 \
        --max-time 30 2>&1) || true

    OIDC_DISCO_CODE=$(echo "$OIDC_DISCO_RESPONSE" | tail -1)
    OIDC_DISCO_BODY=$(echo "$OIDC_DISCO_RESPONSE" | sed '$d')

    if [[ "$OIDC_DISCO_CODE" == "200" ]]; then
        # Validate required OIDC fields
        OIDC_ISSUER=$(echo "$OIDC_DISCO_BODY" | grep -o '"issuer":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "")
        OIDC_AUTH_EP=$(echo "$OIDC_DISCO_BODY" | grep -o '"authorization_endpoint":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "")
        OIDC_TOKEN_EP=$(echo "$OIDC_DISCO_BODY" | grep -o '"token_endpoint":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "")
        OIDC_JWKS_URI=$(echo "$OIDC_DISCO_BODY" | grep -o '"jwks_uri":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "")

        if [[ -n "$OIDC_ISSUER" && -n "$OIDC_AUTH_EP" && -n "$OIDC_TOKEN_EP" && -n "$OIDC_JWKS_URI" ]]; then
            pass "oidc-discovery" "issuer=$OIDC_ISSUER, jwks_uri present, token_endpoint present"
        else
            fail "oidc-discovery" "HTTP 200 but missing required fields (issuer/auth/token/jwks)"
        fi
    else
        fail "oidc-discovery" "HTTP $OIDC_DISCO_CODE from $OIDC_DISCO_URL"
    fi

    # ─── Probe 5: OIDC JWKS Endpoint (signing keys reachable) ─────────────
    if [[ -n "${OIDC_JWKS_URI:-}" ]]; then
        log ""
        log "── Probe 5: OIDC JWKS Endpoint ──"

        OIDC_JWKS_RESPONSE=$(curl -s -w "\n%{http_code}" \
            "$OIDC_JWKS_URI" \
            --connect-timeout 10 \
            --max-time 30 2>&1) || true

        OIDC_JWKS_CODE=$(echo "$OIDC_JWKS_RESPONSE" | tail -1)
        OIDC_JWKS_BODY=$(echo "$OIDC_JWKS_RESPONSE" | sed '$d')

        if [[ "$OIDC_JWKS_CODE" == "200" ]]; then
            OIDC_KEY_COUNT=$(echo "$OIDC_JWKS_BODY" | grep -o '"kid"' | wc -l || echo "0")
            if [[ "$OIDC_KEY_COUNT" -gt 0 ]]; then
                pass "oidc-jwks" "HTTP 200, $OIDC_KEY_COUNT signing key(s) found"
            else
                fail "oidc-jwks" "HTTP 200 but no signing keys (kid) found in JWKS response"
            fi
        else
            fail "oidc-jwks" "HTTP $OIDC_JWKS_CODE from $OIDC_JWKS_URI"
        fi
    else
        skip "oidc-jwks" "Skipped — no jwks_uri from discovery"
    fi

    # ─── Probe 6: OIDC Authorize URL Construction ─────────────────────────
    if [[ -n "${OIDC_AUTH_EP:-}" ]]; then
        log ""
        log "── Probe 6: OIDC Authorize URL Construction (dry-run) ──"

        # Verify the authorize endpoint responds (HEAD or GET without full redirect)
        OIDC_AUTH_CHECK=$(curl -s -o /dev/null -w "%{http_code}" \
            -L --max-redirs 0 \
            "${OIDC_AUTH_EP}?client_id=${OIDC_CLIENT_ID}&response_type=code&scope=openid%20profile%20email&redirect_uri=https://localhost:5001/api/v1/auth/oidc/callback&state=smoke_test&nonce=smoke_nonce" \
            --connect-timeout 10 \
            --max-time 30 2>&1) || true

        # 200, 302, or 303 all indicate the authorize endpoint is alive
        if [[ "$OIDC_AUTH_CHECK" =~ ^(200|302|303|400)$ ]]; then
            pass "oidc-authorize-reachable" "Authorize endpoint responded HTTP $OIDC_AUTH_CHECK (expected for unauthenticated probe)"
        else
            fail "oidc-authorize-reachable" "Authorize endpoint returned HTTP $OIDC_AUTH_CHECK"
        fi
    else
        skip "oidc-authorize-reachable" "Skipped — no authorization_endpoint from discovery"
    fi

    # ─── Probe 7: OIDC Client ID Validation (token endpoint dry call) ─────
    if [[ -n "${OIDC_TOKEN_EP:-}" && -n "${OIDC_CLIENT_SECRET:-}" ]]; then
        log ""
        log "── Probe 7: OIDC Token Endpoint Client Validation ──"

        # Send an intentionally invalid grant to verify the client_id/secret are recognized
        OIDC_TOKEN_CHECK=$(curl -s -w "\n%{http_code}" \
            -X POST "$OIDC_TOKEN_EP" \
            -d "grant_type=authorization_code" \
            -d "code=invalid_smoke_test_code" \
            -d "client_id=$OIDC_CLIENT_ID" \
            -d "client_secret=$OIDC_CLIENT_SECRET" \
            -d "redirect_uri=https://localhost:5001/api/v1/auth/oidc/callback" \
            --connect-timeout 10 \
            --max-time 30 2>&1) || true

        OIDC_TOKEN_CODE=$(echo "$OIDC_TOKEN_CHECK" | tail -1)
        OIDC_TOKEN_BODY=$(echo "$OIDC_TOKEN_CHECK" | sed '$d')

        # We expect a 400 (invalid_grant) — NOT a 401 (bad client credentials)
        if [[ "$OIDC_TOKEN_CODE" == "400" ]]; then
            OIDC_ERR=$(echo "$OIDC_TOKEN_BODY" | grep -o '"error":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "unknown")
            if [[ "$OIDC_ERR" == "invalid_grant" || "$OIDC_ERR" == "invalid_request" ]]; then
                pass "oidc-client-validation" "Client credentials accepted (error=$OIDC_ERR as expected for invalid code)"
            else
                fail "oidc-client-validation" "Unexpected error type: $OIDC_ERR (may indicate client misconfiguration)"
            fi
        elif [[ "$OIDC_TOKEN_CODE" == "401" || "$OIDC_TOKEN_CODE" == "403" ]]; then
            fail "oidc-client-validation" "HTTP $OIDC_TOKEN_CODE — client_id or client_secret rejected by IdP"
        else
            skip "oidc-client-validation" "Unexpected HTTP $OIDC_TOKEN_CODE — cannot determine client validity"
        fi
    else
        skip "oidc-client-validation" "Skipped — OIDC_CLIENT_SECRET not set (optional probe)"
    fi
else
    skip "oidc-discovery" "OIDC credentials not configured"
    skip "oidc-jwks" "OIDC credentials not configured"
    skip "oidc-authorize-reachable" "OIDC credentials not configured"
    skip "oidc-client-validation" "OIDC credentials not configured"
fi

# ═══════════════════════════════════════════════════════════════════════════
#  Report
# ═══════════════════════════════════════════════════════════════════════════

log ""
log "═══════════════════════════════════════════════════════════"
log "  RESULTS SUMMARY"
log "═══════════════════════════════════════════════════════════"

PASS_COUNT=0
FAIL_COUNT=0
SKIP_COUNT=0

for r in "${RESULTS[@]}"; do
    if echo "$r" | grep -q '"result":"pass"'; then
        PASS_COUNT=$((PASS_COUNT + 1))
    elif echo "$r" | grep -q '"result":"fail"'; then
        FAIL_COUNT=$((FAIL_COUNT + 1))
    else
        SKIP_COUNT=$((SKIP_COUNT + 1))
    fi
done

log "  Pass: $PASS_COUNT  Fail: $FAIL_COUNT  Skip: $SKIP_COUNT"
log ""

# Write machine-readable JSON report
{
    echo "{"
    echo "  \"timestamp\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\","
    echo "  \"suite\": \"live-validation-smoke\","
    echo "  \"summary\": {\"pass\": $PASS_COUNT, \"fail\": $FAIL_COUNT, \"skip\": $SKIP_COUNT},"
    echo "  \"probes\": ["
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

log "Report written to: $REPORT_FILE"

if [[ $FAIL_COUNT -gt 0 ]]; then
    log ""
    log "LIVE VALIDATION FAILED ($FAIL_COUNT failure(s))"
    exit 1
fi

log ""
log "LIVE VALIDATION PASSED"
exit 0

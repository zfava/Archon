#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════
#  ArchonAI — OIDC Identity Provider Live Validation Script
# ═══════════════════════════════════════════════════════════════════════════
#
#  Validates an OIDC-compliant Identity Provider by probing its discovery
#  endpoint, JWKS endpoint, and optionally performing a client_credentials
#  token exchange.
#
#  REQUIREMENTS:
#    Environment variables from .env.oidc (see .env.oidc.template)
#
#  USAGE:
#    cp scripts/.env.oidc.template .env.oidc
#    # Fill in values
#    set -a && source .env.oidc && set +a
#    bash scripts/validate-oidc.sh
#
#  EXIT CODES:
#    0  All steps passed
#    1  One or more steps failed
#    2  Missing required environment variables
# ═══════════════════════════════════════════════════════════════════════════

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPORT_DIR="${OIDC_REPORT_DIR:-./live-validation-results}"
mkdir -p "$REPORT_DIR"
REPORT_FILE="$REPORT_DIR/validate-oidc-$(date -u +%Y%m%dT%H%M%SZ).json"

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
    OIDC_ISSUER
    OIDC_CLIENT_ID
    OIDC_CLIENT_SECRET
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
    log "Copy scripts/.env.oidc.template to .env.oidc, fill in values, then:"
    log "  set -a && source .env.oidc && set +a"
    log "  bash scripts/validate-oidc.sh"
    exit 2
fi

OIDC_ISSUER_URL="${OIDC_ISSUER%/}"

log "═══════════════════════════════════════════════════════════"
log "  ArchonAI OIDC Live Validation"
log "  Issuer: $OIDC_ISSUER_URL"
log "═══════════════════════════════════════════════════════════"

# ═══════════════════════════════════════════════════════════════════════════
#  STEP 1: Discovery endpoint (.well-known/openid-configuration)
# ═══════════════════════════════════════════════════════════════════════════

log ""
log "── Step 1: OIDC Discovery Endpoint ──"

DISCO_URL="$OIDC_ISSUER_URL/.well-known/openid-configuration"

DISCO_RESPONSE=$(curl -s -w "\n%{http_code}" \
    "$DISCO_URL" \
    --connect-timeout 10 \
    --max-time 30 2>&1) || true

DISCO_CODE=$(echo "$DISCO_RESPONSE" | tail -1)
DISCO_BODY=$(echo "$DISCO_RESPONSE" | sed '$d')

DISCOVERED_ISSUER=""
JWKS_URI=""
TOKEN_ENDPOINT=""
SUPPORTED_GRANTS=""

if [[ "$DISCO_CODE" == "200" ]]; then
    DISCOVERED_ISSUER=$(echo "$DISCO_BODY" | grep -o '"issuer":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "")
    JWKS_URI=$(echo "$DISCO_BODY" | grep -o '"jwks_uri":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "")
    TOKEN_ENDPOINT=$(echo "$DISCO_BODY" | grep -o '"token_endpoint":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "")
    AUTH_ENDPOINT=$(echo "$DISCO_BODY" | grep -o '"authorization_endpoint":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "")

    # Extract supported grant types
    SUPPORTED_GRANTS=$(echo "$DISCO_BODY" | grep -o '"grant_types_supported":\[[^]]*\]' | head -1 || echo "not advertised")

    if [[ -n "$DISCOVERED_ISSUER" && -n "$JWKS_URI" && -n "$TOKEN_ENDPOINT" ]]; then
        pass "discovery-endpoint" "issuer=$DISCOVERED_ISSUER, jwks_uri present, token_endpoint present"
    else
        MISSING_FIELDS=""
        [[ -z "$DISCOVERED_ISSUER" ]] && MISSING_FIELDS+="issuer "
        [[ -z "$JWKS_URI" ]] && MISSING_FIELDS+="jwks_uri "
        [[ -z "$TOKEN_ENDPOINT" ]] && MISSING_FIELDS+="token_endpoint "
        fail "discovery-endpoint" "HTTP 200 but missing required fields: $MISSING_FIELDS"
    fi
else
    fail "discovery-endpoint" "HTTP $DISCO_CODE from $DISCO_URL"
fi

# ═══════════════════════════════════════════════════════════════════════════
#  STEP 2: JWKS endpoint validation
# ═══════════════════════════════════════════════════════════════════════════

log ""
log "── Step 2: JWKS Endpoint Validation ──"

if [[ -n "$JWKS_URI" ]]; then
    JWKS_RESPONSE=$(curl -s -w "\n%{http_code}" \
        "$JWKS_URI" \
        --connect-timeout 10 \
        --max-time 30 2>&1) || true

    JWKS_CODE=$(echo "$JWKS_RESPONSE" | tail -1)
    JWKS_BODY=$(echo "$JWKS_RESPONSE" | sed '$d')

    if [[ "$JWKS_CODE" == "200" ]]; then
        KEY_COUNT=$(echo "$JWKS_BODY" | grep -o '"kid"' | wc -l || echo "0")
        KEY_COUNT=$(echo "$KEY_COUNT" | tr -d ' ')

        if [[ "$KEY_COUNT" -gt 0 ]]; then
            # Extract key types for reporting
            KEY_TYPES=$(echo "$JWKS_BODY" | grep -o '"kty":"[^"]*"' | cut -d'"' -f4 | sort -u | tr '\n' ',' | sed 's/,$//' || echo "unknown")
            pass "jwks-endpoint" "HTTP 200, $KEY_COUNT signing key(s), types: $KEY_TYPES"
        else
            fail "jwks-endpoint" "HTTP 200 but no signing keys (kid) found"
        fi
    else
        fail "jwks-endpoint" "HTTP $JWKS_CODE from $JWKS_URI"
    fi
else
    fail "jwks-endpoint" "Skipped — no jwks_uri from discovery"
fi

# ═══════════════════════════════════════════════════════════════════════════
#  STEP 3: Client credentials token exchange (if configured)
# ═══════════════════════════════════════════════════════════════════════════

log ""
log "── Step 3: Client Credentials Token Exchange ──"

if [[ -n "$TOKEN_ENDPOINT" && -n "${OIDC_CLIENT_SECRET:-}" ]]; then
    # Determine scope — use configured scope or default to openid
    OIDC_SCOPE="${OIDC_SCOPE:-openid}"

    TOKEN_RESPONSE=$(curl -s -w "\n%{http_code}" \
        -X POST "$TOKEN_ENDPOINT" \
        -d "grant_type=client_credentials" \
        -d "client_id=$OIDC_CLIENT_ID" \
        -d "client_secret=$OIDC_CLIENT_SECRET" \
        -d "scope=$OIDC_SCOPE" \
        --connect-timeout 10 \
        --max-time 30 2>&1) || true

    TOKEN_CODE=$(echo "$TOKEN_RESPONSE" | tail -1)
    TOKEN_BODY=$(echo "$TOKEN_RESPONSE" | sed '$d')

    if [[ "$TOKEN_CODE" == "200" ]]; then
        HAS_ACCESS_TOKEN=$(echo "$TOKEN_BODY" | grep -o '"access_token"' | head -1 || echo "")
        TOKEN_TYPE=$(echo "$TOKEN_BODY" | grep -o '"token_type":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "unknown")
        EXPIRES_IN=$(echo "$TOKEN_BODY" | grep -o '"expires_in":[0-9]*' | head -1 | cut -d: -f2 || echo "?")

        if [[ -n "$HAS_ACCESS_TOKEN" ]]; then
            pass "client-credentials-exchange" "HTTP 200, token_type=$TOKEN_TYPE, expires_in=${EXPIRES_IN}s"
        else
            fail "client-credentials-exchange" "HTTP 200 but no access_token in response"
        fi
    elif [[ "$TOKEN_CODE" == "400" ]]; then
        TOKEN_ERROR=$(echo "$TOKEN_BODY" | grep -o '"error":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "unknown")
        TOKEN_DESC=$(echo "$TOKEN_BODY" | grep -o '"error_description":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "no description")

        if [[ "$TOKEN_ERROR" == "unsupported_grant_type" ]]; then
            pass "client-credentials-exchange" "IdP does not support client_credentials grant (error=$TOKEN_ERROR) — not a configuration issue"
        else
            fail "client-credentials-exchange" "HTTP 400: $TOKEN_ERROR — $TOKEN_DESC"
        fi
    elif [[ "$TOKEN_CODE" == "401" || "$TOKEN_CODE" == "403" ]]; then
        fail "client-credentials-exchange" "HTTP $TOKEN_CODE — client credentials rejected by IdP"
    else
        fail "client-credentials-exchange" "Unexpected HTTP $TOKEN_CODE from token endpoint"
    fi
else
    if [[ -z "$TOKEN_ENDPOINT" ]]; then
        fail "client-credentials-exchange" "Skipped — no token_endpoint from discovery"
    else
        fail "client-credentials-exchange" "Skipped — OIDC_CLIENT_SECRET not set"
    fi
fi

# ═══════════════════════════════════════════════════════════════════════════
#  STEP 4: Report IdP metadata summary
# ═══════════════════════════════════════════════════════════════════════════

log ""
log "── Step 4: IdP Metadata Summary ──"

if [[ -n "$DISCOVERED_ISSUER" ]]; then
    log "  Issuer:           $DISCOVERED_ISSUER"
    log "  JWKS URI:         ${JWKS_URI:-N/A}"
    log "  Token Endpoint:   ${TOKEN_ENDPOINT:-N/A}"
    log "  Auth Endpoint:    ${AUTH_ENDPOINT:-N/A}"
    log "  Supported Grants: ${SUPPORTED_GRANTS:-not advertised}"
    log "  JWKS Key Count:   ${KEY_COUNT:-N/A}"
    pass "idp-metadata" "Issuer=$DISCOVERED_ISSUER, grants=$SUPPORTED_GRANTS, keys=${KEY_COUNT:-0}"
else
    fail "idp-metadata" "Cannot report metadata — discovery failed"
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
    echo "  \"suite\": \"validate-oidc\","
    echo "  \"issuer\": \"${DISCOVERED_ISSUER:-$OIDC_ISSUER_URL}\","
    echo "  \"summary\": {\"pass\": $PASS_COUNT, \"fail\": $FAIL_COUNT},"
    echo "  \"idp_metadata\": {"
    echo "    \"issuer\": \"${DISCOVERED_ISSUER:-}\","
    echo "    \"jwks_uri\": \"${JWKS_URI:-}\","
    echo "    \"token_endpoint\": \"${TOKEN_ENDPOINT:-}\","
    echo "    \"authorization_endpoint\": \"${AUTH_ENDPOINT:-}\","
    echo "    \"supported_grants\": \"${SUPPORTED_GRANTS:-not advertised}\","
    echo "    \"jwks_key_count\": ${KEY_COUNT:-0}"
    echo "  },"
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
    log "OIDC VALIDATION FAILED ($FAIL_COUNT failure(s))"
    exit 1
fi

log ""
log "OIDC VALIDATION PASSED"
exit 0

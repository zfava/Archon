#!/usr/bin/env bash
# ArchonAI Demo Seed Script
# Creates a deterministic demo tenant with sample data for diligence evaluation.
# Usage: ./scripts/demo-seed.sh [API_BASE_URL]
#
# Prerequisites: curl, jq
# Default API: http://localhost:8080

set -euo pipefail

API_BASE="${1:-http://localhost:8080}"
AUTH_BASE="${API_BASE}/api/v1/auth"
API_V1="${API_BASE}/api/v1"

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m'

log()  { echo -e "${GREEN}[SEED]${NC} $*"; }
warn() { echo -e "${YELLOW}[WARN]${NC} $*"; }
fail() { echo -e "${RED}[FAIL]${NC} $*"; exit 1; }

# ── Preflight ────────────────────────────────────────────────────────────────

log "Checking prerequisites..."
command -v curl >/dev/null 2>&1 || fail "curl is required"
command -v jq >/dev/null 2>&1 || fail "jq is required"

log "Waiting for API health check at ${API_BASE}..."
for i in $(seq 1 30); do
    if curl -sf "${API_V1}/health" > /dev/null 2>&1; then
        log "API is healthy."
        break
    fi
    if [ "$i" -eq 30 ]; then
        fail "API did not become healthy within 30 seconds."
    fi
    sleep 1
done

# ── Step 1: Register Demo Organization ───────────────────────────────────────

log "Step 1: Registering demo organization 'Acme Corp'..."
REGISTER_RESPONSE=$(curl -sf -X POST "${AUTH_BASE}/register" \
    -H "Content-Type: application/json" \
    -d '{
        "organizationName": "Acme Corp",
        "email": "admin@acme-demo.com",
        "password": "Demo-P@ssw0rd-2024!",
        "displayName": "Demo Admin"
    }' 2>&1) || warn "Registration may have failed (possibly already exists)"

if [ -n "${REGISTER_RESPONSE:-}" ]; then
    ACCESS_TOKEN=$(echo "$REGISTER_RESPONSE" | jq -r '.tokens.accessToken // empty' 2>/dev/null || true)
    REFRESH_TOKEN=$(echo "$REGISTER_RESPONSE" | jq -r '.tokens.refreshToken // empty' 2>/dev/null || true)
    ORG_ID=$(echo "$REGISTER_RESPONSE" | jq -r '.org.id // empty' 2>/dev/null || true)
    USER_ID=$(echo "$REGISTER_RESPONSE" | jq -r '.user.id // empty' 2>/dev/null || true)

    if [ -n "$ACCESS_TOKEN" ]; then
        log "  Organization registered: ${ORG_ID:-unknown}"
        log "  Admin user created: ${USER_ID:-unknown}"
    fi
fi

# ── Step 2: Login (if registration returned no token) ────────────────────────

if [ -z "${ACCESS_TOKEN:-}" ]; then
    log "Step 2: Logging in as admin@acme-demo.com..."
    LOGIN_RESPONSE=$(curl -sf -X POST "${AUTH_BASE}/login" \
        -H "Content-Type: application/json" \
        -d '{
            "email": "admin@acme-demo.com",
            "password": "Demo-P@ssw0rd-2024!"
        }' 2>&1) || fail "Login failed. Has the demo org been registered?"

    ACCESS_TOKEN=$(echo "$LOGIN_RESPONSE" | jq -r '.tokens.accessToken // empty' 2>/dev/null || true)
    [ -n "$ACCESS_TOKEN" ] || fail "No access token received from login."
    log "  Logged in successfully."
fi

AUTH_HEADER="Authorization: Bearer ${ACCESS_TOKEN}"

# ── Step 3: Verify Identity ─────────────────────────────────────────────────

log "Step 3: Verifying identity..."
ME_RESPONSE=$(curl -sf "${AUTH_BASE}/me" -H "$AUTH_HEADER" 2>&1) || warn "Identity check failed"
if [ -n "${ME_RESPONSE:-}" ]; then
    ME_EMAIL=$(echo "$ME_RESPONSE" | jq -r '.email // "unknown"' 2>/dev/null || echo "unknown")
    log "  Authenticated as: ${ME_EMAIL}"
fi

# ── Step 4: Check Platform Health ────────────────────────────────────────────

log "Step 4: Checking platform health..."
HEALTH=$(curl -sf "${API_V1}/health" 2>&1) || warn "Health check failed"
if [ -n "${HEALTH:-}" ]; then
    log "  Health: $(echo "$HEALTH" | jq -r '.status // "unknown"' 2>/dev/null || echo "unknown")"
fi

# ── Step 5: Query Agent Registry ─────────────────────────────────────────────

log "Step 5: Querying agent registry..."
AGENTS=$(curl -sf "${API_V1}/registry/agents" -H "$AUTH_HEADER" 2>&1) || warn "Agent registry query failed"
if [ -n "${AGENTS:-}" ]; then
    AGENT_COUNT=$(echo "$AGENTS" | jq 'length // 0' 2>/dev/null || echo "0")
    log "  Registered agents: ${AGENT_COUNT}"
fi

# ── Step 6: Check Connector Status ──────────────────────────────────────────

log "Step 6: Checking connector status..."
for connector in salesforce hubspot quickbooks slack; do
    STATUS=$(curl -sf "${API_V1}/connectors/${connector}/status" -H "$AUTH_HEADER" 2>&1) || true
    if [ -n "${STATUS:-}" ]; then
        CONNECTED=$(echo "$STATUS" | jq -r '.isConnected // .isAuthenticated // "unknown"' 2>/dev/null || echo "unknown")
        log "  ${connector}: connected=${CONNECTED}"
    else
        warn "  ${connector}: unreachable"
    fi
done

# ── Step 7: Check Audit Trail ────────────────────────────────────────────────

log "Step 7: Checking audit trail..."
AUDIT=$(curl -sf "${API_V1}/audit/status" -H "$AUTH_HEADER" 2>&1) || warn "Audit status query failed"
if [ -n "${AUDIT:-}" ]; then
    log "  Audit status: $(echo "$AUDIT" | jq -c '.' 2>/dev/null || echo "unavailable")"
fi

# ── Step 8: Invite a Second User (Operator) ──────────────────────────────────

log "Step 8: Inviting operator user..."
INVITE_RESPONSE=$(curl -sf -X POST "${AUTH_BASE}/invite" \
    -H "$AUTH_HEADER" \
    -H "Content-Type: application/json" \
    -d '{
        "email": "operator@acme-demo.com",
        "role": "Operator"
    }' 2>&1) || warn "Invite may have failed"

if [ -n "${INVITE_RESPONSE:-}" ]; then
    INVITE_TOKEN=$(echo "$INVITE_RESPONSE" | jq -r '.inviteToken // empty' 2>/dev/null || true)
    if [ -n "$INVITE_TOKEN" ]; then
        log "  Invite token generated for operator@acme-demo.com"

        # Accept the invite
        ACCEPT_RESPONSE=$(curl -sf -X POST "${AUTH_BASE}/accept-invite" \
            -H "Content-Type: application/json" \
            -d "{
                \"inviteToken\": \"${INVITE_TOKEN}\",
                \"password\": \"Operator-P@ssw0rd-2024!\",
                \"displayName\": \"Demo Operator\"
            }" 2>&1) || warn "Invite acceptance may have failed"

        if [ -n "${ACCEPT_RESPONSE:-}" ]; then
            log "  Operator user joined organization"
        fi
    fi
fi

# ── Step 9: Query Workflows ─────────────────────────────────────────────────

log "Step 9: Querying workflows..."
WORKFLOWS=$(curl -sf "${API_V1}/workflows" -H "$AUTH_HEADER" 2>&1) || warn "Workflow query failed"
if [ -n "${WORKFLOWS:-}" ]; then
    WF_COUNT=$(echo "$WORKFLOWS" | jq 'if type == "array" then length else 0 end' 2>/dev/null || echo "0")
    log "  Active workflows: ${WF_COUNT}"
fi

# ── Summary ──────────────────────────────────────────────────────────────────

echo ""
log "═══════════════════════════════════════════════════════════"
log "  Demo seed complete."
log ""
log "  Demo Organization: Acme Corp"
log "  Admin:    admin@acme-demo.com    / Demo-P@ssw0rd-2024!"
log "  Operator: operator@acme-demo.com / Operator-P@ssw0rd-2024!"
log ""
log "  API Base: ${API_BASE}"
log "  Health:   ${API_V1}/health"
log "═══════════════════════════════════════════════════════════"

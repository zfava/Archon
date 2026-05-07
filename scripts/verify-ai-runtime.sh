#!/usr/bin/env bash
# ArchonAI Runtime Verification Script
# ------------------------------------
# Validates that AI model providers are correctly configured and operational.
# Run this before demos, deployments, or diligence reviews.
#
# Usage:
#   ./scripts/verify-ai-runtime.sh [BASE_URL]
#
# Requires: curl, jq
# Default BASE_URL: http://localhost:5000

set -euo pipefail

BASE_URL="${1:-http://localhost:5000}"
PASS=0
FAIL=0
WARN=0

green()  { printf '\033[0;32m%s\033[0m\n' "$1"; }
red()    { printf '\033[0;31m%s\033[0m\n' "$1"; }
yellow() { printf '\033[0;33m%s\033[0m\n' "$1"; }

check() {
    local label="$1" condition="$2"
    if [ "$condition" = "true" ]; then
        green "  [PASS] $label"
        PASS=$((PASS + 1))
    else
        red "  [FAIL] $label"
        FAIL=$((FAIL + 1))
    fi
}

warn() {
    local label="$1"
    yellow "  [WARN] $label"
    WARN=$((WARN + 1))
}

echo "╔══════════════════════════════════════════════════════════╗"
echo "║          ArchonAI Runtime Verification                  ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""
echo "Target: $BASE_URL"
echo "Time:   $(date -u '+%Y-%m-%dT%H:%M:%SZ')"
echo ""

# ── Step 1: Basic connectivity ──────────────────────────────────
echo "── Step 1: Connectivity ──"
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" "$BASE_URL/healthz/live" 2>/dev/null || echo "000")
check "Liveness endpoint reachable" "$([ "$HTTP_CODE" = "200" ] && echo true || echo false)"

if [ "$HTTP_CODE" = "000" ]; then
    red "Cannot reach $BASE_URL — is the application running?"
    echo ""
    echo "Result: FAIL (0 passed, 1 failed)"
    exit 1
fi

# ── Step 2: Environment report ──────────────────────────────────
echo ""
echo "── Step 2: Environment Validation ──"

# Note: This endpoint requires authentication. If running locally without auth,
# the endpoint should be accessible. In production, provide a bearer token.
ENV_RESPONSE=$(curl -s "$BASE_URL/api/v1/ai-runtime/environment" 2>/dev/null || echo "{}")

if echo "$ENV_RESPONSE" | jq -e '.readinessTier' > /dev/null 2>&1; then
    TIER=$(echo "$ENV_RESPONSE" | jq -r '.readinessTier')
    CLOUD_ACTIVE=$(echo "$ENV_RESPONSE" | jq -r '.cloudProvidersActive')
    LOCAL_ACTIVE=$(echo "$ENV_RESPONSE" | jq -r '.localProviderActive')
    DEFAULT_MODEL=$(echo "$ENV_RESPONSE" | jq -r '.defaultModel')
    SUMMARY=$(echo "$ENV_RESPONSE" | jq -r '.readinessSummary')

    echo "  Readiness Tier:      $TIER"
    echo "  Cloud Providers:     $CLOUD_ACTIVE active"
    echo "  Local Provider:      $LOCAL_ACTIVE"
    echo "  Default Model:       $DEFAULT_MODEL"
    echo "  Summary:             $SUMMARY"
    echo ""

    check "At least one cloud provider configured" "$([ "$CLOUD_ACTIVE" -ge 1 ] && echo true || echo false)"
    check "Default model is not local" "$(echo "$DEFAULT_MODEL" | grep -qiv 'local' && echo true || echo false)"

    if [ "$TIER" = "production" ]; then
        check "Production readiness tier (multi-provider redundancy)" "true"
    elif [ "$TIER" = "production-single" ]; then
        warn "Single cloud provider — consider adding a second for redundancy"
        check "At least production-single tier" "true"
    elif [ "$TIER" = "local-only" ]; then
        warn "Local-only tier — NOT suitable for production"
        check "Cloud provider configured" "false"
    else
        check "Environment configured" "false"
    fi

    # Per-provider status
    echo ""
    echo "  Provider Status:"
    echo "$ENV_RESPONSE" | jq -r '.providers[] | "    \(.name): \(.status) \(if .failureReason then "(\(.failureReason))" else "" end)"'
else
    warn "Could not retrieve environment report (authentication required or endpoint unavailable)"
fi

# ── Step 3: Configuration validation ────────────────────────────
echo ""
echo "── Step 3: Configuration Safety ──"

# Check environment variables
check "OPENAI_API_KEY is set" "$([ -n "${OPENAI_API_KEY:-}" ] && echo true || echo false)"
check "ANTHROPIC_API_KEY is set" "$([ -n "${ANTHROPIC_API_KEY:-}" ] && echo true || echo false)"

if [ -z "${OPENAI_API_KEY:-}" ] && [ -z "${ANTHROPIC_API_KEY:-}" ] && [ -z "${AZURE_OPENAI_API_KEY:-}" ]; then
    red "  CRITICAL: No cloud provider API keys are set!"
    red "  Set OPENAI_API_KEY and/or ANTHROPIC_API_KEY for production."
fi

# ── Summary ─────────────────────────────────────────────────────
echo ""
echo "╔══════════════════════════════════════════════════════════╗"
echo "║  VERIFICATION SUMMARY                                   ║"
echo "╠══════════════════════════════════════════════════════════╣"
printf "║  Passed: %-3d  Failed: %-3d  Warnings: %-3d              ║\n" "$PASS" "$FAIL" "$WARN"
echo "╚══════════════════════════════════════════════════════════╝"

if [ "$FAIL" -gt 0 ]; then
    red "Result: FAIL"
    exit 1
else
    green "Result: PASS"
    exit 0
fi

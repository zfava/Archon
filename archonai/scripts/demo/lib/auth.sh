#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════
# ArchonAI Demo — Authentication Helpers
# Register, login, and token management for demo scenarios.
# ═══════════════════════════════════════════════════════════════
set -euo pipefail

# Base URL (override with ARCHON_API_URL env var)
API_URL="${ARCHON_API_URL:-http://localhost:8080}"

# ───────────────────────────────────────────────────────────────
# api_call METHOD PATH [DATA] [TOKEN]
#   Make an API call and return body + status code.
#   Body is output on stdout; status code is last line.
#   Usage:
#     response=$(api_call GET /api/v1/health "" "$TOKEN")
#     body=$(echo "$response" | sed '$d')
#     status=$(echo "$response" | tail -1)
# ───────────────────────────────────────────────────────────────
api_call() {
  local method="$1"
  local path="$2"
  local data="${3:-}"
  local token="${4:-}"

  local curl_args=(
    -s
    -w '\n%{http_code}'
    -X "$method"
    -H "Content-Type: application/json"
  )

  if [[ -n "$token" ]]; then
    curl_args+=(-H "Authorization: Bearer $token")
  fi

  if [[ -n "$data" && "$method" != "GET" && "$method" != "DELETE" ]]; then
    curl_args+=(-d "$data")
  fi

  curl "${curl_args[@]}" "${API_URL}${path}" 2>/dev/null || echo -e "\n000"
}

# ───────────────────────────────────────────────────────────────
# extract_body RESPONSE
#   Extract the JSON body from an api_call response.
# ───────────────────────────────────────────────────────────────
extract_body() {
  echo "$1" | sed '$d'
}

# ───────────────────────────────────────────────────────────────
# extract_status RESPONSE
#   Extract the HTTP status code from an api_call response.
# ───────────────────────────────────────────────────────────────
extract_status() {
  echo "$1" | tail -1
}

# ───────────────────────────────────────────────────────────────
# register_user ORG_NAME EMAIL PASSWORD DISPLAY_NAME
#   Register a new user and organization.
#   Returns: full response (body + status)
# ───────────────────────────────────────────────────────────────
register_user() {
  local org="$1" email="$2" password="$3" display_name="$4"
  local payload
  payload=$(jq -n \
    --arg org "$org" \
    --arg email "$email" \
    --arg pw "$password" \
    --arg name "$display_name" \
    '{organizationName: $org, email: $email, password: $pw, displayName: $name}')
  api_call POST "/api/auth/register" "$payload"
}

# ───────────────────────────────────────────────────────────────
# login_user EMAIL PASSWORD
#   Login and return the full response (body + status).
#   Extract token with: extract_body "$response" | jq -r '.token'
# ───────────────────────────────────────────────────────────────
login_user() {
  local email="$1" password="$2"
  local payload
  payload=$(jq -n --arg e "$email" --arg p "$password" '{email: $e, password: $p}')
  api_call POST "/api/auth/login" "$payload"
}

# ───────────────────────────────────────────────────────────────
# get_token EMAIL PASSWORD
#   Login and return just the JWT token string.
# ───────────────────────────────────────────────────────────────
get_token() {
  local email="$1" password="$2"
  local response
  response=$(login_user "$email" "$password")
  extract_body "$response" | jq -r '.token // .accessToken // .access_token // empty'
}

# ───────────────────────────────────────────────────────────────
# invite_user EMAIL ROLE TOKEN
#   Invite a user (admin-only).
# ───────────────────────────────────────────────────────────────
invite_user() {
  local email="$1" role="$2" token="$3"
  local payload
  payload=$(jq -n --arg e "$email" --arg r "$role" '{email: $e, role: $r}')
  api_call POST "/api/auth/invite" "$payload" "$token"
}

# ───────────────────────────────────────────────────────────────
# accept_invite EMAIL PASSWORD DISPLAY_NAME INVITE_CODE
#   Accept an invitation and create account.
# ───────────────────────────────────────────────────────────────
accept_invite() {
  local email="$1" password="$2" display_name="$3" code="$4"
  local payload
  payload=$(jq -n \
    --arg e "$email" \
    --arg p "$password" \
    --arg n "$display_name" \
    --arg c "$code" \
    '{email: $e, password: $p, displayName: $n, inviteCode: $c}')
  api_call POST "/api/auth/accept-invite" "$payload"
}

# ───────────────────────────────────────────────────────────────
# wait_for_health [MAX_WAIT_SECONDS]
#   Wait until the API health endpoint returns 200.
# ───────────────────────────────────────────────────────────────
wait_for_health() {
  local max_wait="${1:-120}"
  local elapsed=0
  echo -n "  Waiting for API health"
  while [[ $elapsed -lt $max_wait ]]; do
    local status
    status=$(curl -s -o /dev/null -w '%{http_code}' "${API_URL}/healthz/live" 2>/dev/null || echo "000")
    if [[ "$status" == "200" ]]; then
      echo " OK (${elapsed}s)"
      return 0
    fi
    echo -n "."
    sleep 2
    elapsed=$((elapsed + 2))
  done
  echo " TIMEOUT after ${max_wait}s"
  return 1
}

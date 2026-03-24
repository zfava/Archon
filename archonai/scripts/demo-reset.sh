#!/usr/bin/env bash
# ArchonAI Demo Reset Script
# Tears down and rebuilds the entire platform to a clean state, then re-seeds.
# Usage: ./scripts/demo-reset.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ARCHON_DIR="$(dirname "$SCRIPT_DIR")"

GREEN='\033[0;32m'
RED='\033[0;31m'
NC='\033[0m'

log()  { echo -e "${GREEN}[RESET]${NC} $*"; }
fail() { echo -e "${RED}[FAIL]${NC} $*"; exit 1; }

log "Stopping all services and removing volumes..."
cd "$ARCHON_DIR"
docker compose down -v --remove-orphans 2>/dev/null || true

log "Rebuilding and starting services..."
docker compose up --build -d

log "Waiting for platform to become healthy..."
for i in $(seq 1 60); do
    if curl -sf http://localhost:8080/api/v1/health > /dev/null 2>&1; then
        log "Platform is healthy."
        break
    fi
    if [ "$i" -eq 60 ]; then
        fail "Platform did not become healthy within 60 seconds."
    fi
    sleep 1
done

log "Running demo seed..."
bash "$SCRIPT_DIR/demo-seed.sh"

log "Reset complete. Platform is ready for demo."

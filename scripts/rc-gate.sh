#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# rc-gate.sh — Local release-candidate validation gate for ArchonAI
#
# Usage:
#   ./scripts/rc-gate.sh              # Run all stages
#   ./scripts/rc-gate.sh --skip-docker # Skip Docker builds (faster)
#   ./scripts/rc-gate.sh --stage 3     # Run only stage 3 (integration)
#
# Produces:
#   rc-output/                         # All proof artifacts
#   rc-output/trx/                     # Per-project TRX files
#   rc-output/rc-verdict.json          # Machine-readable verdict
#   rc-output/rc-report.txt            # Operator-readable report
#
# Exit codes:
#   0 = RC is shippable
#   1 = RC is NOT shippable
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SOLUTION="$REPO_ROOT/archonai/ArchonAI.slnx"
OUTPUT_DIR="$REPO_ROOT/rc-output"
TRX_DIR="$OUTPUT_DIR/trx"
SKIP_DOCKER=false
SINGLE_STAGE=""

# Parse arguments
while [[ $# -gt 0 ]]; do
  case $1 in
    --skip-docker) SKIP_DOCKER=true; shift ;;
    --stage) SINGLE_STAGE="$2"; shift 2 ;;
    -h|--help)
      echo "Usage: $0 [--skip-docker] [--stage N]"
      echo "  --skip-docker  Skip Docker image builds"
      echo "  --stage N      Run only stage N (1-8)"
      exit 0
      ;;
    *) echo "Unknown option: $1"; exit 1 ;;
  esac
done

# Prepare output directory
rm -rf "$OUTPUT_DIR"
mkdir -p "$TRX_DIR"

# Stage tracking
declare -A STAGE_RESULTS
STAGES=(
  "1:Build"
  "2:Unit Tests"
  "3:Integration & RuntimeProof"
  "4:AI Runtime Truth"
  "5:Migration Completeness"
  "6:Docker Build"
  "7:Security Scan"
  "8:Config Health"
)

should_run() {
  [ -z "$SINGLE_STAGE" ] || [ "$SINGLE_STAGE" = "$1" ]
}

header() {
  echo ""
  echo "════════════════════════════════════════════════════════════"
  echo "  Stage $1: $2"
  echo "════════════════════════════════════════════════════════════"
}

# ═══════════════════════════════════════════════════════════════════════════
# Stage 1: Build
# ═══════════════════════════════════════════════════════════════════════════
if should_run 1; then
  header 1 "Build"
  if dotnet build "$SOLUTION" -c Release -warnaserror 2>&1 | tail -5; then
    STAGE_RESULTS[1]="pass"
    echo "  ✓ Build passed"
  else
    STAGE_RESULTS[1]="fail"
    echo "  ✗ Build FAILED"
  fi
else
  STAGE_RESULTS[1]="skip"
fi

# ═══════════════════════════════════════════════════════════════════════════
# Stage 2: Unit Tests (per-project, no filter noise)
# ═══════════════════════════════════════════════════════════════════════════
if should_run 2; then
  header 2 "Unit Tests"

  UNIT_FAIL=0

  # Projects with only unit tests — no filter needed, no noise
  UNIT_ONLY=(
    "archonai/tests/ArchonAI.ActionSafety.Tests/ArchonAI.ActionSafety.Tests.csproj"
    "archonai/tests/ArchonAI.AdminAPI.Tests/ArchonAI.AdminAPI.Tests.csproj"
    "archonai/tests/ArchonAI.Agents.Finance.Tests/ArchonAI.Agents.Finance.Tests.csproj"
    "archonai/tests/ArchonAI.Agents.Marketing.Tests/ArchonAI.Agents.Marketing.Tests.csproj"
    "archonai/tests/ArchonAI.Agents.Operations.Tests/ArchonAI.Agents.Operations.Tests.csproj"
    "archonai/tests/ArchonAI.Agents.Sales.Tests/ArchonAI.Agents.Sales.Tests.csproj"
    "archonai/tests/ArchonAI.Agents.Support.Tests/ArchonAI.Agents.Support.Tests.csproj"
    "archonai/tests/ArchonAI.Connectors.Tests/ArchonAI.Connectors.Tests.csproj"
    "archonai/tests/ArchonAI.ProofAnalytics.Tests/ArchonAI.ProofAnalytics.Tests.csproj"
    "archonai/tests/ArchonAI.WorkflowDesigner.Tests/ArchonAI.WorkflowDesigner.Tests.csproj"
  )

  for proj in "${UNIT_ONLY[@]}"; do
    NAME=$(basename "$(dirname "$proj")")
    echo "  ── $NAME ──"
    if dotnet test "$REPO_ROOT/$proj" -c Release \
      --logger "trx;LogFileName=${NAME}.trx" \
      --results-directory "$TRX_DIR" 2>&1 | tail -3; then
      echo "    ✓ passed"
    else
      echo "    ✗ FAILED"
      UNIT_FAIL=$((UNIT_FAIL + 1))
    fi
  done

  # Mixed projects — filter out Integration/RuntimeProof
  MIXED=(
    "archonai/tests/ArchonAI.Tests/ArchonAI.Tests.csproj"
    "archonai/tests/ArchonAI.Enterprise.Tests/ArchonAI.Enterprise.Tests.csproj"
  )

  for proj in "${MIXED[@]}"; do
    NAME=$(basename "$(dirname "$proj")")
    echo "  ── $NAME (unit only) ──"
    if dotnet test "$REPO_ROOT/$proj" -c Release \
      --filter "Category!=Integration&Category!=RuntimeProof" \
      --logger "trx;LogFileName=${NAME}-unit.trx" \
      --results-directory "$TRX_DIR" 2>&1 | tail -3; then
      echo "    ✓ passed"
    else
      echo "    ✗ FAILED"
      UNIT_FAIL=$((UNIT_FAIL + 1))
    fi
  done

  if [ $UNIT_FAIL -eq 0 ]; then
    STAGE_RESULTS[2]="pass"
    echo "  ✓ All unit tests passed"
  else
    STAGE_RESULTS[2]="fail"
    echo "  ✗ $UNIT_FAIL project(s) had failures"
  fi
else
  STAGE_RESULTS[2]="skip"
fi

# ═══════════════════════════════════════════════════════════════════════════
# Stage 3: Integration + RuntimeProof Tests
# ═══════════════════════════════════════════════════════════════════════════
if should_run 3; then
  header 3 "Integration & RuntimeProof"

  if ! docker version > /dev/null 2>&1; then
    echo "  ✗ Docker not available — cannot run Testcontainers tests"
    STAGE_RESULTS[3]="fail"
  else
    INT_FAIL=0

    echo "  ── Enterprise Integration Tests ──"
    if dotnet test "$REPO_ROOT/archonai/tests/ArchonAI.Enterprise.Tests/ArchonAI.Enterprise.Tests.csproj" \
      -c Release \
      --filter "Category=Integration" \
      --logger "trx;LogFileName=enterprise-integration.trx" \
      --logger "console;verbosity=normal" \
      --results-directory "$TRX_DIR" 2>&1; then
      echo "    ✓ Integration tests passed"
    else
      echo "    ✗ Integration tests FAILED"
      INT_FAIL=$((INT_FAIL + 1))
    fi

    echo ""
    echo "  ── Enterprise RuntimeProof Tests ──"
    if dotnet test "$REPO_ROOT/archonai/tests/ArchonAI.Enterprise.Tests/ArchonAI.Enterprise.Tests.csproj" \
      -c Release \
      --filter "Category=RuntimeProof" \
      --logger "trx;LogFileName=enterprise-runtimeproof.trx" \
      --logger "console;verbosity=normal" \
      --results-directory "$TRX_DIR" 2>&1; then
      echo "    ✓ RuntimeProof tests passed"
    else
      echo "    ✗ RuntimeProof tests FAILED"
      INT_FAIL=$((INT_FAIL + 1))
    fi

    # Verify execution proof
    for TRX_NAME in enterprise-integration enterprise-runtimeproof; do
      TRX_FILE="$TRX_DIR/${TRX_NAME}.trx"
      if [ -f "$TRX_FILE" ]; then
        TOTAL=$(grep -oP 'total="\K[0-9]+' "$TRX_FILE" | head -1 || echo "0")
        PASSED=$(grep -oP 'passed="\K[0-9]+' "$TRX_FILE" | head -1 || echo "0")
        echo "  ${TRX_NAME}: total=${TOTAL} passed=${PASSED}"
        if [ "${TOTAL:-0}" -eq 0 ] || [ "${PASSED:-0}" -eq 0 ]; then
          echo "  ✗ ${TRX_NAME}: tests did not actually execute"
          INT_FAIL=$((INT_FAIL + 1))
        fi
      else
        echo "  ✗ ${TRX_NAME}: TRX file missing"
        INT_FAIL=$((INT_FAIL + 1))
      fi
    done

    if [ $INT_FAIL -eq 0 ]; then
      STAGE_RESULTS[3]="pass"
    else
      STAGE_RESULTS[3]="fail"
    fi
  fi
else
  STAGE_RESULTS[3]="skip"
fi

# ═══════════════════════════════════════════════════════════════════════════
# Stage 4: AI Runtime Truth Checks
# ═══════════════════════════════════════════════════════════════════════════
if should_run 4; then
  header 4 "AI Runtime Truth"

  if dotnet test "$REPO_ROOT/archonai/tests/ArchonAI.Tests/ArchonAI.Tests.csproj" \
    -c Release \
    --filter "Category=RuntimeProof" \
    --logger "trx;LogFileName=ai-runtime-proof.trx" \
    --results-directory "$TRX_DIR" 2>&1 | tail -5; then

    TRX_FILE="$TRX_DIR/ai-runtime-proof.trx"
    if [ -f "$TRX_FILE" ]; then
      PASSED=$(grep -oP 'passed="\K[0-9]+' "$TRX_FILE" | head -1 || echo "0")
      echo "  ✓ AI runtime truth: ${PASSED} checks passed"
      STAGE_RESULTS[4]="pass"
    else
      echo "  ✗ TRX missing"
      STAGE_RESULTS[4]="fail"
    fi
  else
    STAGE_RESULTS[4]="fail"
    echo "  ✗ AI runtime truth checks FAILED"
  fi
else
  STAGE_RESULTS[4]="skip"
fi

# ═══════════════════════════════════════════════════════════════════════════
# Stage 5: Migration Completeness
# ═══════════════════════════════════════════════════════════════════════════
if should_run 5; then
  header 5 "Migration Completeness"

  SCRIPTS_DIR="$REPO_ROOT/archonai/src/ArchonAI.Migrations/Scripts"
  DOWN_DIR="$REPO_ROOT/archonai/src/ArchonAI.Migrations/Down"
  MIG_FAIL=0

  UP_COUNT=$(ls "$SCRIPTS_DIR"/*.sql 2>/dev/null | wc -l)
  DOWN_COUNT=$(ls "$DOWN_DIR"/*.sql 2>/dev/null | wc -l)
  echo "  Up migrations:    $UP_COUNT"
  echo "  Rollback scripts: $DOWN_COUNT"

  # Check rollback coverage
  for up_file in "$SCRIPTS_DIR"/*.sql; do
    NUM=$(basename "$up_file" | grep -oP '^\d+')
    TABLE=$(basename "$up_file" | sed 's/^[0-9]*_create_//' | sed 's/\.sql$//')
    DOWN_FILE="$DOWN_DIR/${NUM}_drop_${TABLE}.sql"
    if [ ! -f "$DOWN_FILE" ]; then
      echo "  ✗ Missing rollback: $NUM ($TABLE)"
      MIG_FAIL=$((MIG_FAIL + 1))
    fi
  done

  # Check sequential numbering
  EXPECTED=1
  for up_file in $(ls "$SCRIPTS_DIR"/*.sql | sort); do
    NUM=$(basename "$up_file" | grep -oP '^\d+' | sed 's/^0*//')
    if [ "$NUM" -ne "$EXPECTED" ]; then
      echo "  ✗ Gap: expected $(printf '%03d' $EXPECTED), found $(printf '%03d' $NUM)"
      MIG_FAIL=$((MIG_FAIL + 1))
    fi
    EXPECTED=$((EXPECTED + 1))
  done

  # Check non-empty
  for f in "$SCRIPTS_DIR"/*.sql "$DOWN_DIR"/*.sql; do
    if [ ! -s "$f" ]; then
      echo "  ✗ Empty: $(basename "$f")"
      MIG_FAIL=$((MIG_FAIL + 1))
    fi
  done

  if [ $MIG_FAIL -eq 0 ]; then
    STAGE_RESULTS[5]="pass"
    echo "  ✓ All $UP_COUNT migrations have rollbacks, sequential, non-empty"
  else
    STAGE_RESULTS[5]="fail"
  fi
else
  STAGE_RESULTS[5]="skip"
fi

# ═══════════════════════════════════════════════════════════════════════════
# Stage 6: Docker Build Validity
# ═══════════════════════════════════════════════════════════════════════════
if should_run 6; then
  if [ "$SKIP_DOCKER" = true ]; then
    echo ""
    echo "  ⊘ Docker stage skipped (--skip-docker)"
    STAGE_RESULTS[6]="skip"
  elif ! docker version > /dev/null 2>&1; then
    echo "  ✗ Docker not available"
    STAGE_RESULTS[6]="fail"
  else
    header 6 "Docker Build"

    DOCKER_FAIL=0
    IMAGES=(
      "api:archonai/deploy/docker/Dockerfile.api"
      "cli:archonai/deploy/docker/Dockerfile.cli"
      "runtime:archonai/deploy/docker/Dockerfile.runtime"
      "scheduler:archonai/deploy/docker/Dockerfile.scheduler"
      "agents:archonai/deploy/docker/Dockerfile.agents"
      "gateway:archonai/deploy/docker/Dockerfile.gateway"
    )

    for entry in "${IMAGES[@]}"; do
      NAME="${entry%%:*}"
      DOCKERFILE="${entry#*:}"
      TAG="archonai-${NAME}:rc-validate"

      echo "  ── $NAME ──"
      if docker build -f "$REPO_ROOT/$DOCKERFILE" -t "$TAG" "$REPO_ROOT" 2>&1 | tail -3; then
        USER=$(docker inspect --format '{{.Config.User}}' "$TAG" 2>/dev/null || echo "")
        if [ -z "$USER" ] || [ "$USER" = "root" ]; then
          echo "    ✗ runs as root"
          DOCKER_FAIL=$((DOCKER_FAIL + 1))
        else
          echo "    ✓ built, user=$USER"
        fi
      else
        echo "    ✗ build FAILED"
        DOCKER_FAIL=$((DOCKER_FAIL + 1))
      fi
    done

    if [ $DOCKER_FAIL -eq 0 ]; then
      STAGE_RESULTS[6]="pass"
      echo "  ✓ All 6 images built, non-root"
    else
      STAGE_RESULTS[6]="fail"
    fi
  fi
else
  STAGE_RESULTS[6]="skip"
fi

# ═══════════════════════════════════════════════════════════════════════════
# Stage 7: Security Scan
# ═══════════════════════════════════════════════════════════════════════════
if should_run 7; then
  header 7 "Security Scan"

  dotnet list "$REPO_ROOT/archonai/" package --vulnerable --include-transitive 2>&1 | tee "$OUTPUT_DIR/nuget-vuln-report.txt"

  if grep -q "has the following vulnerable packages" "$OUTPUT_DIR/nuget-vuln-report.txt"; then
    STAGE_RESULTS[7]="fail"
    echo "  ✗ Vulnerable NuGet packages detected"
  else
    STAGE_RESULTS[7]="pass"
    echo "  ✓ No vulnerable NuGet packages"
  fi

  # Secret scan
  EXCLUDE_PATTERN="\.env\.example|SECRETS\.md|ci-cd\.yml|rc-validate\.yml|rc-gate\.sh|\.git/"
  if grep -rn --include="*.yaml" --include="*.yml" --include="*.json" --include="*.cs" --include="*.xml" \
    -E '(password|secret|apikey|api_key|signing_key|token).*[:=]\s*"[^"${}]{8,}"' \
    --exclude-dir=.git --exclude="*.md" "$REPO_ROOT" 2>/dev/null | grep -v -E "$EXCLUDE_PATTERN" | grep -v '""' | head -5; then
    echo "  ⚠ Potential hardcoded secrets (review above)"
  else
    echo "  ✓ No hardcoded secrets"
  fi
else
  STAGE_RESULTS[7]="skip"
fi

# ═══════════════════════════════════════════════════════════════════════════
# Stage 8: Config Health
# ═══════════════════════════════════════════════════════════════════════════
if should_run 8; then
  header 8 "Config Health"

  if dotnet test "$REPO_ROOT/archonai/tests/ArchonAI.Enterprise.Tests/ArchonAI.Enterprise.Tests.csproj" \
    -c Release \
    --filter "Category!=Integration&Category!=RuntimeProof" \
    --logger "trx;LogFileName=config-security.trx" \
    --results-directory "$TRX_DIR" 2>&1 | tail -5; then

    TRX_FILE="$TRX_DIR/config-security.trx"
    if [ -f "$TRX_FILE" ]; then
      PASSED=$(grep -oP 'passed="\K[0-9]+' "$TRX_FILE" | head -1 || echo "0")
      echo "  ✓ Config/security: ${PASSED} checks passed"
      STAGE_RESULTS[8]="pass"
    else
      STAGE_RESULTS[8]="fail"
    fi
  else
    STAGE_RESULTS[8]="fail"
    echo "  ✗ Config health FAILED"
  fi
else
  STAGE_RESULTS[8]="skip"
fi

# ═══════════════════════════════════════════════════════════════════════════
# Verdict
# ═══════════════════════════════════════════════════════════════════════════
echo ""
echo "╔══════════════════════════════════════════════════════════════╗"
echo "║           ARCHON RC VALIDATION VERDICT                      ║"
echo "╠══════════════════════════════════════════════════════════════╣"
printf "║  %-58s ║\n" "Commit: $(git -C "$REPO_ROOT" rev-parse --short HEAD 2>/dev/null || echo 'unknown')"
printf "║  %-58s ║\n" "Date:   $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "╠══════════════════════════════════════════════════════════════╣"

ALL_PASS=true
for entry in "${STAGES[@]}"; do
  NUM="${entry%%:*}"
  LABEL="${entry#*:}"
  RESULT="${STAGE_RESULTS[$NUM]:-skip}"

  case "$RESULT" in
    pass) ICON="✓ PASS" ;;
    skip) ICON="⊘ SKIP" ;;
    *)    ICON="✗ FAIL"; ALL_PASS=false ;;
  esac

  printf "║  Stage %s: %-30s %s\n" "$NUM" "$LABEL" "$ICON"
done

echo "╠══════════════════════════════════════════════════════════════╣"

if [ "$ALL_PASS" = true ]; then
  echo "║                                                              ║"
  echo "║    VERDICT:  ✓  RC IS SHIPPABLE                              ║"
  echo "║                                                              ║"
else
  echo "║                                                              ║"
  echo "║    VERDICT:  ✗  RC IS NOT SHIPPABLE                          ║"
  echo "║                                                              ║"
fi

echo "╠══════════════════════════════════════════════════════════════╣"
echo "║  External credential gaps:                                    ║"
echo "║    - AI: OPENAI_API_KEY / ANTHROPIC_API_KEY required           ║"
echo "║    - Connectors: OAuth credentials for live validation         ║"
echo "║    - OIDC: live IdP for end-to-end auth flow                   ║"
echo "║    - Vault: ISecretProvider abstraction exists, no impl yet    ║"
echo "╚══════════════════════════════════════════════════════════════╝"

# Write machine-readable verdict
cat > "$OUTPUT_DIR/rc-verdict.json" <<EOF
{
  "sha": "$(git -C "$REPO_ROOT" rev-parse HEAD 2>/dev/null || echo 'unknown')",
  "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "stages": {
$(for entry in "${STAGES[@]}"; do
    NUM="${entry%%:*}"
    LABEL="${entry#*:}"
    RESULT="${STAGE_RESULTS[$NUM]:-skip}"
    echo "    \"stage_${NUM}_$(echo "$LABEL" | tr ' &' '_' | tr '[:upper:]' '[:lower:]')\": \"$RESULT\","
  done)
    "_sentinel": true
  },
  "verdict": "$([ "$ALL_PASS" = true ] && echo 'shippable' || echo 'not_shippable')"
}
EOF

# Write operator-readable report
{
  echo "ArchonAI RC Validation Report"
  echo "Generated: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo ""
  echo "Artifacts produced:"
  echo "  rc-output/trx/           — TRX test result files (per-project)"
  echo "  rc-output/rc-verdict.json — Machine-readable verdict"
  echo "  rc-output/rc-report.txt  — This report"
  if [ -f "$OUTPUT_DIR/nuget-vuln-report.txt" ]; then
    echo "  rc-output/nuget-vuln-report.txt — NuGet vulnerability scan"
  fi
  echo ""
  echo "TRX files produced:"
  for f in "$TRX_DIR"/*.trx 2>/dev/null; do
    [ -f "$f" ] || continue
    TOTAL=$(grep -oP 'total="\K[0-9]+' "$f" | head -1 || echo "?")
    PASSED=$(grep -oP 'passed="\K[0-9]+' "$f" | head -1 || echo "?")
    FAILED=$(grep -oP 'failed="\K[0-9]+' "$f" | head -1 || echo "?")
    printf "  %-45s total=%-4s passed=%-4s failed=%-4s\n" "$(basename "$f")" "$TOTAL" "$PASSED" "$FAILED"
  done
} > "$OUTPUT_DIR/rc-report.txt"

cat "$OUTPUT_DIR/rc-report.txt"

if [ "$ALL_PASS" != true ]; then
  exit 1
fi

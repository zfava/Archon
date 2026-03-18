# ArchonAI — Demo & Sandbox Guide

## For: Solutions Architects, Technical Evaluators, Demo Engineers

This guide provides two demo paths: a 10-minute executive overview and a 30-minute technical deep-dive. Both use the same local environment with zero external dependencies.

---

## Prerequisites

| Requirement | Version | Check |
|---|---|---|
| Docker + Docker Compose | 24+ | `docker --version` |
| .NET SDK | 10.0 | `dotnet --version` |
| Git | 2.30+ | `git --version` |
| Available ports | 8080, 5432, 4222, 8222 | `lsof -i :8080` |

**Optional (for live AI):**
- OpenAI API key → set `ModelProviders__OpenAI__ApiKey`
- Anthropic API key → set `ModelProviders__Anthropic__ApiKey`
- Azure OpenAI endpoint + key → set `ModelProviders__AzureOpenAI__Endpoint` and `ApiKey`
- Local Ollama instance at `http://localhost:11434`

Without API keys, the platform runs fully but AI responses are echo-back stubs.

---

## Quick Start

### 1. Start the Platform

```bash
# Clone and start
cd archonai
docker compose up --build -d

# Wait for health checks (PostgreSQL readiness takes ~15 seconds)
docker compose ps

# Verify API is ready
curl -s http://localhost:8080/api/v1/health | jq .
```

Expected health response:
```json
{
  "status": "Healthy",
  "checks": {
    "event_bus": "Healthy",
    "task_queue": "Healthy",
    "connectors": "Healthy",
    "model_providers": "Healthy",
    "startup_readiness": "Healthy"
  }
}
```

### 2. Run Enterprise Verification Tests

```bash
# Run all 168 tests — proves enterprise claims without the platform running
dotnet test tests/ArchonAI.Enterprise.Tests/ --verbosity normal
```

All tests run in < 3 seconds with zero external dependencies.

### 3. Stop the Platform

```bash
docker compose down        # Stop services, keep data
docker compose down -v     # Stop services, delete data (full reset)
```

---

## Executive Demo Path (10 minutes)

**Audience:** CTO, VP Engineering, Investor

**Goal:** Show architecture maturity, security depth, and governance rigor without requiring deep technical knowledge.

### Step 1: Enterprise Test Suite (2 min)

```bash
cd archonai
dotnet test tests/ArchonAI.Enterprise.Tests/ --verbosity normal
```

**Talking points:**
- 168 tests, all green, < 3 seconds
- Tests map to 57 specific enterprise claims (see `docs/enterprise/enterprise-proof-pack.md`)
- Zero external dependencies — no database, no network, no API keys needed

### Step 2: Security Attack Coverage (3 min)

```bash
dotnet test tests/ArchonAI.Enterprise.Tests/ --filter "Namespace~Security" --verbosity normal
```

**Talking points:**
- 58 security tests across 5 suites
- JWT forgery, expired tokens, "none" algorithm bypass — all blocked
- SQL injection (6 variants), XSS (5 variants), null byte injection — all safe
- Cross-tenant access prevention — 8 attack vectors blocked
- RBAC boundary enforcement — privilege escalation impossible

### Step 3: Architecture Overview (3 min)

Open `docs/diligence/README.md` and walk through:
- 59-project modular solution
- 7 deployable services
- 10 enterprise connectors
- 8-phase intelligence loop

### Step 4: Honest Gaps (2 min)

Open the "Weakest Diligence Impressions" section of `docs/diligence/README.md`:
- No live AI without API keys
- In-memory persistence
- No SSO
- Secrets in plaintext
- No load testing

**Why this matters:** Showing gaps proactively builds trust faster than hiding them.

---

## Technical Diligence Demo Path (30 minutes)

**Audience:** Solutions Architect, Security Engineer, Platform Engineer

**Goal:** Verify claims through code inspection, test execution, and live platform interaction.

### Phase 1: Test Evidence (5 min)

```bash
cd archonai

# Integration tests — workflow, RBAC, policy, audit, connectors
dotnet test tests/ArchonAI.Enterprise.Tests/ --filter "Namespace~Integration" --verbosity normal

# Security tests — auth bypass, cross-tenant, permissions, input validation, config
dotnet test tests/ArchonAI.Enterprise.Tests/ --filter "Namespace~Security" --verbosity normal

# End-to-end tests — workflow+governance, auth session lifecycle
dotnet test tests/ArchonAI.Enterprise.Tests/ --filter "Namespace~EndToEnd" --verbosity normal

# API contract tests — response shape stability
dotnet test tests/ArchonAI.Enterprise.Tests/ --filter "Namespace~Contract" --verbosity normal
```

### Phase 2: Live Platform (10 min)

```bash
# Start platform
docker compose up --build -d

# Check health
curl -s http://localhost:8080/api/v1/health | jq .

# Check NATS monitoring
curl -s http://localhost:8222/varz | jq '{server_id, version, jetstream}'
```

**Explore API endpoints:**

```bash
# Gateway routes — 17 API route groups
curl -s http://localhost:8080/api/v1/registry/agents | jq .
curl -s http://localhost:8080/api/v1/workflows | jq .
curl -s http://localhost:8080/api/v1/connectors/status | jq .
curl -s http://localhost:8080/api/v1/audit/status | jq .
```

### Phase 3: Code Inspection (10 min)

**Workflow State Machine:**
```bash
# Verify state transitions are deterministic
cat src/ArchonAI.Workflow/WorkflowStateMachine.cs
# Look for: AllowedTransitions dictionary, TryTransition method
```

**RBAC Implementation:**
```bash
# Verify permission evaluation
cat src/ArchonAI.Api/Security/RbacService.cs
# Look for: system role seeding, deny-overrides-allow, immutability checks
```

**Tenant Isolation:**
```bash
# Verify AsyncLocal scope management
cat src/ArchonAI.MultiTenant/MultiTenantContext.cs
# Look for: AsyncLocal<string?>, scope disposal, nested scope support
```

**Audit Hash Chain:**
```bash
# Verify SHA-256 chain integrity
cat src/ArchonAI.Trace/AuditLogService.cs
# Look for: ComputeHash method, PreviousEntryId linking
```

### Phase 4: Deployment Review (5 min)

```bash
# Docker Compose topology
cat docker-compose.yml

# Kubernetes manifests
ls deploy/kubernetes/

# Helm chart values
cat deploy/helm/archonai/values.yaml
# Look for: resource limits, replica counts, health probes
```

---

## What to Expect vs What Not to Expect

### You Will See

| Feature | What Happens |
|---|---|
| Health checks | All 5 subsystems report Healthy |
| API responses | Structured JSON responses from all 17 route groups |
| Test execution | 168 tests pass in < 3 seconds |
| Docker stack | 7 containers running with health checks |
| Configuration | 40+ config sections with documented defaults |

### You Will NOT See

| Feature | Why Not |
|---|---|
| AI-generated reasoning | No API keys configured by default. Model providers return echo stubs. |
| Persisted state across restarts | Core services use in-memory stores. `docker compose down -v` resets everything. |
| SSO login flow | Not implemented. JWT tokens are issued directly. |
| Real connector data | Connectors require live API credentials (Salesforce, HubSpot, etc.). |
| Load test results | No performance testing exists. |

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `docker compose up` fails | Port conflict | `lsof -i :8080` and kill the conflicting process |
| PostgreSQL health check fails | Slow startup | Wait 30 seconds and check `docker compose ps` |
| Tests fail with build error | Missing .NET SDK | Install .NET 10 SDK |
| API returns 500 | First request before initialization | Wait for startup health check, retry |
| NATS connection refused | Container not ready | Check `docker compose logs nats` |

## Resetting to Clean State

```bash
# Full reset — remove all containers, volumes, and images
docker compose down -v --rmi local

# Rebuild from scratch
docker compose up --build -d
```

---

## Next Steps After Demo

| Goal | Action |
|---|---|
| Verify enterprise claims | Read `docs/enterprise/enterprise-proof-pack.md` |
| Review security posture | Read `docs/diligence/security-summary.md` |
| Understand architecture | Read `docs/diligence/technical-summary.md` |
| Run with live AI | Set `ModelProviders__OpenAI__ApiKey` environment variable in `docker-compose.yml` |
| Deploy to Kubernetes | Follow `deploy/helm/archonai/README.md` |

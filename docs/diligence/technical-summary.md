# ArchonAI — Technical Summary

## For: Buyer, Investor, CTO, Solutions Architect

This document provides a factual assessment of what ArchonAI implements today, what is partially built, and what remains on the roadmap. Every status is verifiable through code inspection or test execution.

---

## Technology Stack

| Layer | Technology | Version |
|---|---|---|
| Language | C# | .NET 10 |
| API | ASP.NET Core REST | v1/v2 versioned |
| Gateway | YARP reverse proxy | Built-in |
| Database | PostgreSQL + pgvector | 16 |
| Message broker | NATS JetStream | 2.10 |
| Observability | OpenTelemetry + Serilog | Prometheus export |
| Container runtime | Docker | Multi-stage builds |
| Orchestration | Kubernetes + Helm | Chart v0.3.0 |
| Test framework | xUnit + NSubstitute + FluentAssertions | Latest |

---

## Implementation Status Matrix

### Fully Implemented (code + tests + configuration)

| Component | Description | Test Evidence | Code Location |
|---|---|---|---|
| **Workflow Engine** | Deterministic state machine: Created → Planning → Scheduled → Executing → Evaluating → Completed/Failed → Escalated. Invalid transitions rejected. Concurrent-safe. | 13 integration tests | `src/ArchonAI.Workflow/` |
| **Durable Execution** | File-backed persistence, step-level retry, idempotency guards, crash recovery | 5 E2E tests | `src/ArchonAI.WorkflowRuntime/` |
| **RBAC** | 3 system roles (Admin, Operator, Viewer), 16 permissions, deny-overrides-allow, immutable system roles | 23 integration + security tests | `src/ArchonAI.Api/Security/` |
| **Multi-Tenant Isolation** | AsyncLocal scope management, per-tenant resource quotas (planning slots, task counts), cross-tenant access prevention | 22 integration + security tests | `src/ArchonAI.MultiTenant/` |
| **Governance Gates** | Approval workflows with separation of duties, self-approval blocking, role-gated approvals, tenant-scoped deduplication | 16 tests | `src/ArchonAI.Governance/` |
| **Policy Engine** | Multi-factor risk scoring, forbidden capability blocking, confidence thresholds, manual overrides, stacking risk factors | 12 tests | `src/ArchonAI.Policy/` |
| **Audit Trail** | SHA-256 hash-chained entries, integrity verification, category/subject/time querying, pagination | 10 tests | `src/ArchonAI.Trace/` |
| **JWT Authentication** | HMAC-SHA256 signing, refresh token rotation, invite flows, token lifecycle management | 22 tests (11 attack vectors) | `src/ArchonAI.Identity/` |
| **Input Validation** | SQL injection prevention, XSS handling, null byte defense, boundary value handling, unicode support | 16 tests | `src/ArchonAI.Identity/` |
| **Connector Framework** | 6 specialized connectors (Salesforce, HubSpot, QuickBooks, Slack, Microsoft 365, Google Workspace) with OAuth, retry, rate limiting | 8 resilience tests | `src/ArchonAI.Connectors/` |
| **Intelligence Loop** | 8-phase autonomous cycle with full event bus instrumentation | Loop integration present in E2E | `src/ArchonAI.IntelligenceLoop/` |
| **API Gateway** | YARP reverse proxy, 17 route groups, tiered rate limiting (120/60 req/min), JWT validation | Configuration verified | `src/ArchonAI.Gateway/` |
| **Deployment** | Docker Compose (7 services), Kubernetes manifests, Helm chart with resource limits and health probes | Manifests present | `docker-compose.yml`, `deploy/` |

### Partially Implemented

| Component | What Works | What's Missing | Risk Level |
|---|---|---|---|
| **LLM Model Providers** | 4 providers (OpenAI, Anthropic, Azure OpenAI, Local/Ollama) with real HTTP client code, retry logic, and error handling. Model router with task-type routing. Echo stubs explicitly labeled with `FinishReason: "echo_fallback"`. | Config-dependent — requires API keys at deployment. No AI-generated reasoning without keys. | **Critical** |
| **Connector Live Validation** | 10 connectors (6 specialized + 4 generic) with real HTTP clients, OAuth, Polly circuit breakers, rate limiting, and audit events. | All tested with mock HTTP handlers only — no live API sandbox validation. | **Medium** |
| **Observability Pipeline** | OpenTelemetry tracing + metrics, Serilog structured logging, Prometheus export, 5 API health checks, worker health endpoints, 10 Grafana dashboard JSON files, Prometheus alert rules. | Grafana dashboards not validated against live Prometheus scrape. No log aggregation pipeline (EFK/Loki). | **Low** |
| **Secret Management** | `ISecretProvider` abstraction with `ChainedSecretProvider` (File → Environment chain) and `RotatingJwtSecurityKeyProvider`. | No external vault integration (HashiCorp Vault, AWS SM, Azure KV). Helm supports external-secrets operator but app reads from chain only. | **Medium** |

### Implemented Since Prior Audit (No Longer Gaps)

> **Note:** The following were previously listed as "Not Implemented" or "Partially Implemented" and are now source-complete or runtime-proven. See `/docs/diligence/runtime-truth-summary.md` for evidence tiers.

| Component | Current State | Evidence |
|---|---|---|
| **Database Persistence** | 22 PostgreSQL-backed stores via `ReplaceWithFactory`. Config-driven factory: PostgreSQL when connection string set, in-memory fallback otherwise. | `DependencyInjection.cs` — 22 registrations. Multi-instance tests pass. |
| **Database Migrations** | DbUp framework with 25 numbered SQL scripts (001–025), journal table, transaction-per-script, rollback scripts. | `ArchonAI.Migrations/Scripts/`, `MigrationRunner.cs`, `MigrationHealthCheck.cs` |
| **SSO/OIDC** | Full OIDC federation: JWKS signature verification, nonce validation, JIT user provisioning, per-tenant IdP config, external identity linking. | `OidcTokenExchangeService.cs`, 3 test classes. Tested with mock IdPs — not validated against live Okta/Entra/Auth0. |
| **MFA** | TOTP and WebAuthn (FIDO2): setup, verify, disable, recovery codes (PBKDF2), org-level policy, admin reset. | `TotpService.cs`, `WebAuthnService.cs`, 6 test classes. |
| **Circuit Breaker** | Polly pipeline: Timeout → Bulkhead → Circuit Breaker. Per-integration state tracking, event bus notifications. | `ResiliencePipelineFactory.cs`, `CircuitBreakerTests.cs` |
| **Container Security Scanning** | Trivy in CI/CD. Scans API and Agents images, SARIF output, fails on CRITICAL/HIGH. | `.github/workflows/ci-cd.yml` |
| **Dependency Vulnerability Scanning** | `dotnet list package --vulnerable --include-transitive` in CI. Separate dependency-review workflow. | `.github/workflows/ci-cd.yml`, `.github/workflows/dependency-review.yml` |
| **Load/Performance Testing** | k6 infrastructure with 4 scenarios (agent-execution-stress, connector-resilience, governance-load, soak-test). | `.github/workflows/load-test.yml`. **No published baselines** — infrastructure exists, results not captured. |

---

## Architecture Decisions

### Why These Choices Were Made

| Decision | Rationale | Trade-off |
|---|---|---|
| **59-project modular solution** | Each domain boundary is a separate project. Enforces compile-time dependency rules. | Build time increases with project count. |
| **YARP gateway** | Native .NET reverse proxy. No external dependency (nginx, Envoy). | Less community tooling than Envoy/Istio. |
| **NATS JetStream** | Lightweight message broker with persistence. Simpler than Kafka for current scale. | Less ecosystem maturity than Kafka/RabbitMQ. |
| **AsyncLocal for tenant context** | Zero-allocation tenant propagation through async call chains. | Requires discipline — easy to forget scope disposal. |
| **In-memory stores as default** | Fast iteration, zero-dependency testing. | Production readiness requires migration to PostgreSQL. |
| **Sharded task queues (64 shards)** | Reduces contention under high agent volume. | Memory overhead for low-volume deployments. |
| **Deterministic state machine** | Prevents impossible workflow states. Every transition is validated. | More rigid than event-sourced approaches. |

---

## Configuration Overview

### Runtime Configuration

| Section | Key Defaults | Configurable |
|---|---|---|
| **Runtime** | MaxDegreeOfParallelism: 128, MaxRetries: 2, BaseRetryDelay: 250ms, QueueShards: 64 | Yes |
| **Policy** | MinConfidence: 0.65, AutoBlockRisk: 80, ApprovalRisk: 60, HighRiskCapabilities: 3 defined | Yes |
| **Scheduler** | MaxGpuSlots: 4, MaxParallelTasks: 128, DefaultPriority: 100 | Yes |
| **Governance** | MaxConcurrentExecutions: 20/agent, MaxExecutions/Hour: 500/agent | Yes |
| **Supervisor** | MaxExecutionsInWindow: 50, RunawayWindow: 60s, ConsecutiveFailuresBeforeRestart: 3 | Yes |
| **Sandbox** | DefaultMemoryLimit: 1024MB, DefaultCpuQuota: 70%, NetworkAccess: disabled | Yes |
| **MultiTenant** | MaxConcurrentPlans: 10/tenant, MaxTasks/Plan: 500/tenant | Yes |
| **Authentication** | AccessToken: 30min, RefreshToken: 7 days, InviteToken: 7 days | Yes |
| **Rate Limiting** | Standard: 120 req/min (queue 20), Admin: 60 req/min (queue 10) | Yes |

### Model Routing

| Optimization | Provider | Model |
|---|---|---|
| Default | Local (Ollama) | local.default |
| Cost-optimized | Local | local.default |
| Latency-optimized | Azure OpenAI | gpt-4o-mini |
| Quality-optimized | OpenAI | gpt-4.1 |

**Task-type routing:**
- analysis → openai.gpt-4.1
- classification → azure.gpt-4o-mini
- extraction → anthropic.claude-3-5-sonnet
- drafting → openai.gpt-4.1
- lightweight → local.default

---

## Deployment Topology

### Local Development (Docker Compose)

| Service | Image | Ports | Health Check |
|---|---|---|---|
| Gateway | archonai/gateway:local | 8080 | Depends on API |
| API | archonai/api:local | 8080 (internal) | pg_isready on postgres |
| Runtime | archonai/runtime:local | — | Depends on postgres, nats |
| Scheduler | archonai/scheduler:local | — | Depends on postgres, nats |
| Agents | archonai/agents:local | — | Depends on postgres, nats |
| PostgreSQL | pgvector/pgvector:pg16 | 5432 | pg_isready (5s/5s/20 retries) |
| NATS | nats:2.10-alpine | 4222, 8222 | JetStream enabled |

### Production (Kubernetes/Helm)

| Component | Replicas | CPU (req/limit) | Memory (req/limit) |
|---|---|---|---|
| Gateway | 2 | 250m / 1 core | 256Mi / 512Mi |
| API | 2 | 250m / 1 core | 512Mi / 1Gi |
| Runtime | 2 | 500m / 2 cores | 1Gi / 2Gi |
| Scheduler | 1 | 250m / 1 core | 512Mi / 1Gi |
| Agents | 3 | 500m / 2 cores | 1Gi / 2Gi |

**Total cluster baseline:** 10 pods, ~4.5 CPU cores requested, ~4.5Gi memory requested.

---

## Assumptions and Limitations

### Assumptions

1. **API keys provided at deployment** — LLM providers require valid API keys for real AI output. Without keys, the system runs but produces echo-back responses.
2. **PostgreSQL available** — The platform assumes a PostgreSQL 16+ instance with pgvector extension.
3. **NATS available** — Inter-service communication requires NATS JetStream.
4. **Single-region deployment** — Helm chart targets a single Kubernetes cluster. Multi-region is not addressed.

### Known Limitations

1. **No AI reasoning without API keys** — The intelligence loop, agent execution, and planning pipeline are structurally complete but produce echo-stub output without configured API keys. Stubs are labeled with `FinishReason: "echo_fallback"`.
2. **No external vault integration** — `ISecretProvider` chain exists (File → Environment) but no HashiCorp Vault, AWS SM, or Azure KV provider. Helm supports external-secrets operator at the infrastructure level.
3. **No published load test baselines** — k6 test infrastructure exists with 4 scenarios but no baseline results have been captured or published.
4. **OIDC not validated against live IdPs** — Federation is implemented and tested with mock IdPs. Not confirmed against Okta, Entra ID, or Auth0.
5. **Durable workflow step persistence is per-instance** — `DurableWorkflowExecutionEngine` uses file-backed step state, not shared across instances in multi-replica deployments.
6. **PostgreSQL and NATS are single-instance** — Helm chart deploys single PostgreSQL and NATS instances. HA requires external managed services (RDS, Cloud SQL, NATS cluster).

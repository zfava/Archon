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
| Orchestration | Kubernetes + Helm | Chart v0.2.0 |
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
| **LLM Model Providers** | 4 providers (OpenAI, Anthropic, Azure OpenAI, Local/Ollama) with real HTTP client code, retry logic, and error handling. Model router with task-type routing. | Default fallback is echo stub when API keys absent. No AI-generated reasoning in default demo. | **Critical** |
| **Database Persistence** | PostgreSQL connection strings configured. pgvector extension for vector search. Memory persistence layer exists. | No migration files. Core services (RBAC, audit, governance) use `ConcurrentDictionary`. State lost on restart. | **Critical** |
| **Generic Connectors** | CRM, ERP, Financial, Messaging connectors with standard interface, retry logic, and audit events. | HTTP responses are deterministic stubs, not connected to real APIs. | **Medium** |
| **Observability** | OpenTelemetry tracing + metrics registered. Serilog structured logging. Prometheus export configured. Health checks for 5 subsystems. | No Grafana dashboards, no alerting rules, no log aggregation pipeline. | **Low** |

### Not Implemented (Roadmap)

| Component | Current State | Impact |
|---|---|---|
| **SSO/OIDC** | JWT issuance exists. No Okta/Entra/Auth0 integration. | Blocks enterprise pilot without workaround. |
| **MFA** | Not present. | Compliance gap for regulated industries. |
| **Secret Management** | API keys and signing keys in `appsettings.json` and Helm values. | Security risk in production. |
| **Database Migrations** | Schema uses `CREATE TABLE IF NOT EXISTS`. No version tracking. | Upgrade path undefined. |
| **Load/Performance Testing** | Zero load tests. | No evidence of behavior at scale. |
| **Container Security Scanning** | No CVE checking in CI/CD. | Supply chain risk. |
| **Dependency Vulnerability Scanning** | No `dotnet list package --vulnerable`. | Known-CVE risk. |
| **Circuit Breaker** | Retry logic exists. No circuit breaker under sustained failure. | Cascading failure risk. |

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

1. **No AI reasoning without API keys** — This is the single largest truth gap. The intelligence loop, agent execution, and planning pipeline are structurally complete but produce no AI-generated output in default configuration.
2. **State volatility** — Core services use in-memory stores. A restart loses all RBAC assignments, audit entries, governance decisions, and agent registrations.
3. **No schema migration path** — Upgrading the database schema requires manual intervention.
4. **Secrets exposure** — All credentials stored in plaintext configuration files.
5. **No horizontal scaling proof** — Resource limits are configured but never tested under load.

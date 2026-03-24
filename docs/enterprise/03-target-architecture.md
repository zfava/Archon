# 03 - Enterprise Target-State Architecture

> Blueprint for converting ArchonAI from a functional prototype to an enterprise-grade platform.
> Audit date: 2026-03-17

---

## Architecture Principles

1. **No in-memory defaults** — All state must be durable by default; in-memory mode is dev-only
2. **Zero-trust security** — Every request authenticated, every action authorized, every secret managed
3. **Tenant isolation** — No shared-memory tenant separation; database-level or schema-level isolation
4. **Observable by default** — Every service emits structured logs, metrics, and traces; dashboards and alerts pre-configured
5. **Tested at every layer** — Unit, integration, contract, E2E, and load tests with minimum coverage gates
6. **Deployable anywhere** — Kubernetes-native with Helm, IaC, and GitOps; multi-region capable

---

## Target State by Subsystem

### 1. API Gateway

| Aspect | Current | Target |
|--------|---------|--------|
| Routing | YARP reverse proxy | YARP with service discovery |
| AuthN | JWT Bearer validation | JWT + OIDC/SAML token exchange |
| Rate limiting | Fixed-window per tier | Per-tenant adaptive rate limiting |
| Versioning | `/api/v1` and `/api/v2` routes | API versioning with deprecation headers |
| Documentation | None | OpenAPI 3.1 spec auto-generated, Swagger UI |
| TLS | Not configured | TLS termination with cert-manager |

### 2. Auth / Identity

| Aspect | Current | Target |
|--------|---------|--------|
| User identity | Missing | OIDC integration (Okta/Entra/Auth0) |
| Agent identity | In-memory store | PostgreSQL-backed identity store |
| Token issuance | Missing | `/auth/token` endpoint with refresh tokens |
| Session management | Missing | Redis-backed session store with sliding expiry |
| MFA | Missing | TOTP/WebAuthn support |
| API keys | Missing | Per-tenant API key issuance and rotation |
| RBAC persistence | ConcurrentDictionary | PostgreSQL with cached lookups |

### 3. Tenancy

| Aspect | Current | Target |
|--------|---------|--------|
| Isolation | Key-prefix decorator | Schema-per-tenant with connection routing |
| Provisioning | Missing | Tenant provisioning API with onboarding workflow |
| Data residency | Not supported | Region-tagged tenants with data routing |
| Encryption | Shared | Per-tenant encryption keys via KMS |
| Resource quotas | In-memory enforcement | Persisted quotas with usage tracking |
| Billing | Missing | Usage metering per tenant |

### 4. Authorization / Policy

| Aspect | Current | Target |
|--------|---------|--------|
| RBAC state | In-memory | PostgreSQL with Redis cache |
| Policy rules | Config-driven | Policy-as-code with OPA integration |
| Approval workflows | State machine in-memory | Durable approval flows with notifications |
| Audit of decisions | Event bus (transient) | Persisted to audit store with immutable log |
| Custom roles | Not supported | Tenant-defined custom roles |

### 5. AI Execution

| Aspect | Current | Target |
|--------|---------|--------|
| Model routing | Adaptive weights in-memory | Persisted performance history + A/B routing |
| LLM integration | Provider references, no validation | Integration tests with real API keys |
| Prompt management | Inline in code | Prompt template registry with versioning |
| Token tracking | Metrics only | Per-request token counting with cost attribution |
| Model fallback | Configured chains | Health-checked fallback with circuit breakers |
| Guardrails | Policy-based | Content filtering + PII detection + output validation |

### 6. Workflow / Runtime

| Aspect | Current | Target |
|--------|---------|--------|
| Workflow state | In-memory | PostgreSQL-backed state machine |
| Task queues | In-memory priority queues | NATS JetStream or Redis Streams with persistence |
| Execution tracking | ConcurrentDictionary | PostgreSQL execution log with telemetry |
| Distributed locking | None | Redis/etcd distributed locks |
| Compensation | Retry with backoff | Saga pattern with compensating transactions |
| Idempotency | Not implemented | Idempotency keys on all mutations |

### 7. Control Plane

| Aspect | Current | Target |
|--------|---------|--------|
| Configuration | IOptions from appsettings | Centralized config service with hot-reload |
| Feature flags | None | LaunchDarkly or self-hosted feature flag service |
| Cluster coordination | In-memory node registry | etcd/Consul-backed service discovery |
| Health checks | Basic HTTP probes | Deep health checks with dependency validation |

### 8. Connectors

| Aspect | Current | Target |
|--------|---------|--------|
| OAuth | Salesforce password flow | OAuth 2.0 PKCE/Authorization Code for all connectors |
| Credential storage | IOptions from config | Vault/KMS-encrypted credential store |
| Webhook ingestion | Not implemented | Webhook receiver with signature verification |
| Schema mapping | Inline | Configurable field mapping registry |
| Sync strategy | On-demand query | Change Data Capture (CDC) + webhook hybrid |
| Error recovery | Retry with backoff | Dead-letter queue with manual replay UI |

### 9. Memory / Knowledge

| Aspect | Current | Target |
|--------|---------|--------|
| Memory persistence | Conditional (Postgres or in-memory) | PostgreSQL always; in-memory for dev only |
| Vector search | pgvector HNSW | pgvector with tuned HNSW params + embedding model integration |
| Embeddings | Character-trigram hash | Real embedding model (OpenAI/Cohere) |
| Knowledge graph | Postgres tables | Postgres with proper graph indexes + optional Neo4j |
| TTL/retention | ExpiresAtUtc field | Automated cleanup job with configurable retention policies |

### 10. Telemetry / Observability

| Aspect | Current | Target |
|--------|---------|--------|
| Tracing | OpenTelemetry configured | Full distributed tracing with Jaeger/Tempo |
| Metrics | Prometheus exporter | Prometheus + Grafana dashboards (pre-built) |
| Logging | Serilog to console | Serilog → OpenTelemetry → Loki/ELK |
| Alerting | None | Grafana alerting with PagerDuty/Opsgenie |
| SLOs | None | Defined SLOs for API latency, task success rate, loop cycle time |
| Cost tracking | None | Per-tenant LLM cost tracking and reporting |

### 11. Frontend App Shell

| Aspect | Current | Target |
|--------|---------|--------|
| Auth UI | Missing | Login page, SSO redirect, session management |
| State management | Local hooks | React Context for auth + React Query for server state |
| Error boundaries | None visible | Global error boundary with fallback UI |
| Loading states | Basic | Skeleton screens, optimistic updates |
| Accessibility | Not audited | WCAG 2.1 AA compliance |
| Testing | None | Vitest unit tests + Playwright E2E |
| Build optimization | Vite defaults | Code splitting, lazy loading, bundle analysis |

### 12. Deployment / Ops

| Aspect | Current | Target |
|--------|---------|--------|
| IaC | Missing | Terraform modules for AWS/GCP/Azure |
| GitOps | Missing | ArgoCD with environment promotion |
| Secrets | Plain text in config | External Secrets Operator + Vault |
| TLS | Not configured | cert-manager with Let's Encrypt |
| Backup | Missing | Automated PostgreSQL backup with PITR |
| DR | Not planned | Multi-region active-passive with RTO/RPO targets |
| Environments | Single | dev → staging → production pipeline |

### 13. Testing / Verification

| Aspect | Current | Target |
|--------|---------|--------|
| Unit tests | 25 files (agents + connectors) | All engines, services, and critical paths |
| Integration tests | Missing | Tests against real PostgreSQL, NATS, connector sandboxes |
| Contract tests | Missing | API contract tests between frontend and backend |
| E2E tests | Missing | Playwright tests for critical user journeys |
| Load tests | Missing | k6/Locust tests for API and intelligence loop throughput |
| Coverage gate | None | Minimum 80% line coverage on PR merge |
| Security scanning | None | Snyk/Trivy for dependencies + SAST |

### 14. Documentation

| Aspect | Current | Target |
|--------|---------|--------|
| Architecture | ARCHITECTURE.md (aspirational) | Updated to reflect actual state; ADRs for decisions |
| API docs | Missing | Auto-generated OpenAPI spec + Swagger UI |
| Runbooks | Missing | Operational runbooks for common incidents |
| Developer guide | Missing | Setup guide, contribution guide, coding standards |
| Security | Missing | Security model documentation, threat model |

---

## Target Deployment Topology

```
                         ┌──────────────┐
                         │  CDN / WAF   │
                         └──────┬───────┘
                                │
                    ┌───────────┴───────────┐
                    │    API Gateway (YARP) │
                    │  TLS + AuthN + Rate   │
                    └───────────┬───────────┘
                                │
           ┌────────────────────┼────────────────────┐
           │                    │                     │
    ┌──────┴──────┐    ┌───────┴───────┐    ┌───────┴───────┐
    │  API Service │    │ Control Plane │    │  Admin API    │
    │  (Stateless) │    │   Service     │    │  (Stateless)  │
    └──────┬──────┘    └───────┬───────┘    └───────────────┘
           │                    │
    ┌──────┴──────────────────┴──────┐
    │        NATS JetStream           │
    │     (Durable Event Bus)         │
    └──────┬──────────┬──────────┬───┘
           │          │          │
    ┌──────┴───┐ ┌────┴────┐ ┌──┴──────────┐
    │ Runtime  │ │Scheduler│ │Agent Workers │
    │ Workers  │ │ Worker  │ │ (Autoscaled) │
    └──────┬───┘ └────┬────┘ └──┬──────────┘
           │          │          │
    ┌──────┴──────────┴──────────┴───┐
    │         Data Layer              │
    │  PostgreSQL   Redis   Vault    │
    │  (pgvector)   (cache) (secrets)│
    └────────────────────────────────┘
```

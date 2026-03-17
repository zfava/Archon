# 04 - Gap Register (Prioritized Remediation Backlog)

> Severity levels:
> - **P0** = Must fix before any enterprise claims are made
> - **P1** = Required for production-capable status
> - **P2** = Required for enterprise-capable status
> - **P3** = Polish or later enhancement

---

## P0 — Must Fix Before Enterprise Claims

### P0-001: No user authentication flow
- **Subsystem:** Auth/Identity
- **Current:** JWT validation middleware exists but no token issuance endpoint, no login flow, no user model
- **Impact:** Cannot authenticate any human user; the system is effectively unauthenticated
- **Fix:** Implement OIDC integration (Okta/Entra/Auth0) with token exchange endpoint, or build JWT issuance with `/auth/token` + `/auth/refresh`
- **Files:** New: `ArchonAI.Identity/UserAuthenticationService.cs`, `ArchonAI.Api/Controllers/AuthController.cs`
- **Effort:** Large (2-3 weeks)

### P0-002: Audit log not persisted
- **Subsystem:** Governance/Audit
- **Current:** `AuditLogService` uses `ConcurrentDictionary` — all audit records lost on restart
- **Impact:** Cannot claim compliance (SOC2, HIPAA, SOX); audit integrity is meaningless without durability
- **Fix:** Persist audit entries to PostgreSQL with append-only table, write-once semantics, checksum column
- **Files:** `archonai/src/ArchonAI.Api/AuditLogService.cs` → new `PostgresAuditLogRepository`
- **Effort:** Small (2-3 days)

### P0-003: RBAC state not persisted
- **Subsystem:** Authorization
- **Current:** `RbacService` stores roles/assignments/policies in `ConcurrentDictionary`
- **Impact:** All role assignments lost on restart; any user/role mapping disappears
- **Fix:** Persist RBAC state to PostgreSQL; load on startup with Redis cache
- **Files:** `archonai/src/ArchonAI.Api/RbacService.cs` → new `PostgresRbacRepository`
- **Effort:** Small (2-3 days)

### P0-004: Secrets in plain text
- **Subsystem:** Security
- **Current:** JWT signing key in env var / appsettings; DB password in Helm values; connector OAuth credentials in config
- **Impact:** Secrets exposed in source control, container env, and K8s configmaps
- **Fix:** Integrate HashiCorp Vault or cloud KMS; use External Secrets Operator for K8s
- **Files:** Helm chart `values.yaml`, all `*Options.cs` credential fields
- **Effort:** Medium (1 week)

### P0-005: No database migration framework
- **Subsystem:** Infrastructure/Data
- **Current:** Schema created via `CREATE TABLE IF NOT EXISTS` in repository constructors
- **Impact:** Cannot evolve schema safely; no rollback; no version tracking
- **Fix:** Adopt DbUp or EF Core migrations; version all schema changes
- **Files:** New: `archonai/src/ArchonAI.Infrastructure/Migrations/`
- **Effort:** Small (3-4 days)

### P0-006: Default configuration is all-in-memory
- **Subsystem:** Infrastructure
- **Current:** Empty `ConnectionString` → in-memory; `UseNats=false` → in-memory event bus
- **Impact:** Default deployment loses all state on restart; misleading for anyone deploying without reading docs
- **Fix:** Make PostgreSQL + NATS the default; require explicit opt-in for in-memory (dev mode)
- **Files:** `archonai/src/ArchonAI.Infrastructure/DependencyInjection.cs`, `appsettings.json`
- **Effort:** Small (1 day)

---

## P1 — Required for Production-Capable Status

### P1-001: Core engine test coverage
- **Subsystem:** Testing
- **Current:** No tests for Orchestrator, Runtime, Policy, Governance, Memory, IntelligenceLoop, Workflow, ModelRouter, Reasoner
- **Impact:** Core execution pipeline is untested; regression risk is high
- **Fix:** Write unit tests for all core engines; target 80% coverage
- **Effort:** Large (2-3 weeks)

### P1-002: Integration tests
- **Subsystem:** Testing
- **Current:** Zero integration tests
- **Impact:** Cannot verify PostgreSQL, NATS, or connector interactions work correctly
- **Fix:** Add integration test project with Testcontainers for PostgreSQL + NATS
- **Effort:** Medium (1-2 weeks)

### P1-003: Workflow state persistence
- **Subsystem:** Workflow/Runtime
- **Current:** `WorkflowEngine` state machine in-memory; execution tracking in ConcurrentDictionary
- **Impact:** Active workflows lost on restart; no recovery
- **Fix:** Persist workflow state to PostgreSQL with state transition log
- **Files:** `archonai/src/ArchonAI.Workflow/WorkflowEngine.cs`
- **Effort:** Medium (1 week)

### P1-004: Governance execution state persistence
- **Subsystem:** Governance
- **Current:** Active execution counts and history in ConcurrentDictionary
- **Impact:** Resource quota enforcement resets on restart; concurrent execution limits bypassed
- **Fix:** Track execution state in Redis (ephemeral) + PostgreSQL (audit)
- **Files:** `archonai/src/ArchonAI.Governance/GovernanceKernel.cs`
- **Effort:** Small (2-3 days)

### P1-005: Agent identity persistence
- **Subsystem:** Identity
- **Current:** `InMemoryAgentIdentityStore` with ConcurrentDictionary
- **Impact:** Agent profiles, capabilities, and performance history lost on restart
- **Fix:** PostgreSQL-backed identity store with cache
- **Files:** `archonai/src/ArchonAI.Identity/InMemoryAgentIdentityStore.cs`
- **Effort:** Small (2-3 days)

### P1-006: OpenAPI specification
- **Subsystem:** API
- **Current:** No API documentation
- **Impact:** External consumers cannot discover or integrate with the API
- **Fix:** Add Swashbuckle/NSwag; annotate controllers; publish Swagger UI
- **Effort:** Small (2-3 days)

### P1-007: Centralized logging
- **Subsystem:** Observability
- **Current:** Serilog to console only
- **Impact:** Cannot search/aggregate logs across services; no log retention
- **Fix:** Route Serilog through OpenTelemetry to Loki or ELK
- **Effort:** Small (2-3 days)

### P1-008: Health check depth
- **Subsystem:** Operations
- **Current:** Basic HTTP health endpoints
- **Impact:** K8s restarts pods based on shallow checks; downstream failures not detected
- **Fix:** Deep health checks validating PostgreSQL, NATS, and critical service connectivity
- **Effort:** Small (1-2 days)

### P1-009: Distributed locking
- **Subsystem:** Runtime
- **Current:** No distributed locks; cluster coordination in-memory
- **Impact:** Multiple instances can execute the same task simultaneously
- **Fix:** Redis-based distributed locking (Redlock) for task dedup and leader election
- **Effort:** Medium (1 week)

### P1-010: Real embedding model
- **Subsystem:** Memory
- **Current:** Character-trigram hash for embeddings
- **Impact:** Semantic search quality is poor; not competitive with real embeddings
- **Fix:** Integrate OpenAI/Cohere embedding API; batch embed on write
- **Files:** `archonai/src/ArchonAI.Memory/OrganizationalMemoryStore.cs`
- **Effort:** Small (2-3 days)

---

## P2 — Required for Enterprise-Capable Status

### P2-001: Multi-tenant database isolation
- **Subsystem:** Tenancy
- **Current:** Key-prefix decorator over shared in-memory/single-schema stores
- **Impact:** Cross-tenant data leakage risk; cannot satisfy enterprise data isolation requirements
- **Fix:** Schema-per-tenant with connection routing; or row-level security with tenant_id columns
- **Effort:** Large (2-3 weeks)

### P2-002: SSO/OIDC integration
- **Subsystem:** Auth
- **Current:** No SSO; no OIDC; no SAML
- **Impact:** Enterprise customers cannot use their existing identity providers
- **Fix:** Implement OIDC Authorization Code flow with PKCE; SAML adapter
- **Effort:** Large (2-3 weeks)

### P2-003: End-to-end tests
- **Subsystem:** Testing
- **Current:** No E2E test framework
- **Impact:** Cannot verify critical user journeys work across frontend + backend
- **Fix:** Playwright tests for onboarding, command execution, dashboard viewing
- **Effort:** Medium (1-2 weeks)

### P2-004: Grafana dashboards and alerting
- **Subsystem:** Observability
- **Current:** Prometheus metrics emitted but no consumption
- **Impact:** No visibility into production health; incidents detected only by users
- **Fix:** Pre-built Grafana dashboards + alerting rules + SLO definitions
- **Effort:** Medium (1 week)

### P2-005: Terraform IaC
- **Subsystem:** Deployment
- **Current:** No infrastructure-as-code
- **Impact:** Cannot reproducibly provision cloud infrastructure
- **Fix:** Terraform modules for AWS/GCP/Azure (VPC, EKS/GKE, RDS, ElastiCache, NATS)
- **Effort:** Large (2-3 weeks)

### P2-006: GitOps deployment
- **Subsystem:** Deployment
- **Current:** CI builds images but no CD pipeline to environments
- **Impact:** Manual deployment process; no environment promotion; no rollback
- **Fix:** ArgoCD with dev → staging → production promotion
- **Effort:** Medium (1-2 weeks)

### P2-007: Connector webhook support
- **Subsystem:** Connectors
- **Current:** On-demand query only; no inbound webhooks
- **Impact:** Cannot receive real-time events from Salesforce, HubSpot, etc.
- **Fix:** Webhook receiver with signature verification; event normalization pipeline
- **Effort:** Medium (1-2 weeks)

### P2-008: Per-tenant API keys
- **Subsystem:** Auth
- **Current:** No API key system
- **Impact:** External integrations cannot authenticate programmatically
- **Fix:** API key issuance, rotation, scoping, and rate limiting per tenant
- **Effort:** Medium (1 week)

### P2-009: Backup and disaster recovery
- **Subsystem:** Ops
- **Current:** No backup procedures
- **Impact:** Data loss on infrastructure failure; no recovery capability
- **Fix:** Automated PostgreSQL backup with PITR; NATS stream replication; recovery runbook
- **Effort:** Medium (1 week)

### P2-010: Frontend auth UI
- **Subsystem:** Frontend
- **Current:** No login page, no auth state, no session management
- **Impact:** Users cannot log in through the UI
- **Fix:** Login page with SSO redirect; auth context provider; protected routes; session refresh
- **Effort:** Medium (1 week)

### P2-011: Load testing
- **Subsystem:** Testing
- **Current:** No load tests
- **Impact:** Unknown performance limits; cannot set capacity planning targets
- **Fix:** k6 or Locust tests for API endpoints and intelligence loop throughput
- **Effort:** Medium (1 week)

### P2-012: Security scanning
- **Subsystem:** Security
- **Current:** No dependency scanning, no SAST
- **Impact:** Unknown vulnerabilities in dependencies
- **Fix:** Snyk/Trivy in CI pipeline; SAST with CodeQL
- **Effort:** Small (2-3 days)

---

## P3 — Polish / Later Enhancement

### P3-001: CLI implementation
- **Current:** `Console.WriteLine("ArchonAI CLI")` only
- **Fix:** Implement CLI with System.CommandLine for ops/admin tasks
- **Effort:** Medium

### P3-002: Plugin system
- **Current:** PluginOptions scaffold only; no plugin loading
- **Fix:** Assembly-based plugin loading with sandboxed execution
- **Effort:** Large

### P3-003: Planner project implementation
- **Current:** Empty project (no .cs files); planning logic exists in StrategicPlanner
- **Fix:** Either implement or remove; resolve naming confusion with StrategicPlanner
- **Effort:** Small

### P3-004: Frontend accessibility audit
- **Current:** Not audited for WCAG compliance
- **Fix:** Audit and remediate for WCAG 2.1 AA
- **Effort:** Medium

### P3-005: API versioning strategy
- **Current:** v1/v2 routes exist but no deprecation headers or migration guide
- **Fix:** Formal versioning policy with sunset headers and migration documentation
- **Effort:** Small

### P3-006: Content filtering / PII detection
- **Current:** No content safety layer
- **Fix:** Add PII detection and content filtering to LLM inputs/outputs
- **Effort:** Medium

### P3-007: Cost attribution and billing
- **Current:** ModelPerformanceTracker tracks cost but no per-tenant attribution
- **Fix:** Per-tenant LLM cost tracking with usage reporting
- **Effort:** Medium

### P3-008: Prompt template registry
- **Current:** Prompts inline in agent code
- **Fix:** Centralized prompt registry with versioning and A/B testing
- **Effort:** Medium

### P3-009: EventBus project cleanup
- **Current:** Empty project; functionality in Infrastructure
- **Fix:** Remove empty project to reduce confusion
- **Effort:** Trivial

---

## Summary by Priority

| Priority | Count | Effort Range |
|----------|-------|-------------|
| P0 | 6 | 4-7 weeks total |
| P1 | 10 | 6-10 weeks total |
| P2 | 12 | 12-18 weeks total |
| P3 | 9 | 6-10 weeks total |
| **Total** | **37** | **28-45 weeks** |

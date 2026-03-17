# 05 - Enterprise Phase Plan

> Implementation-ready execution plan for converting ArchonAI to enterprise-grade.
> Audit date: 2026-03-17

---

## Phase Overview

```
Phase 0: Truth Lock (2 weeks)
    → Make the system honest about what it is
Phase 1: Production Foundation (4-6 weeks)
    → Core persistence, auth, testing, observability
Phase 2: Enterprise Hardening (6-8 weeks)
    → Multi-tenant isolation, SSO, secrets, DR
Phase 3: Scale & Polish (4-6 weeks)
    → Load testing, IaC, GitOps, docs, compliance
```

---

## Phase 0: Truth Lock (Weeks 1-2)

**Goal:** Eliminate all P0 gaps. Make the system durable and honest.

### Sprint 0.1 — Persistence defaults (3 days)

| Task | Owner | Deliverable |
|------|-------|-------------|
| Flip default to PostgreSQL + NATS | Backend | Modified `DependencyInjection.cs`; in-memory requires `--dev-mode` flag |
| Implement database migrations | Backend | DbUp project with versioned SQL scripts |
| Persist audit log to PostgreSQL | Backend | `PostgresAuditLogRepository` with append-only table |
| Persist RBAC state to PostgreSQL | Backend | `PostgresRbacRepository` with Redis cache |

**Acceptance criteria:**
- System requires PostgreSQL connection string to start (or explicit `--dev-mode`)
- Audit records survive pod restart
- Role/assignment changes survive pod restart
- `dotnet ef migrations list` shows version history

### Sprint 0.2 — LLM Providers, Auth & Secrets (5 days)

| Task | Owner | Deliverable |
|------|-------|-------------|
| Implement real LLM model providers | Backend | Wire `OpenAiModelProvider`, `AnthropicModelProvider`, `AzureOpenAiModelProvider` to make actual HTTP calls using the already-registered `HttpClient` |
| Implement JWT token issuance | Backend | `/auth/token` endpoint with username/password |
| Add refresh token flow | Backend | `/auth/refresh` endpoint with rotation |
| Integrate Vault for secrets (incl. LLM API keys) | DevOps | Vault sidecar injector or External Secrets Operator |
| Remove plain-text secrets from Helm | DevOps | Secrets reference Vault paths |
| Add basic login page | Frontend | Login form → `/auth/token` → store in httpOnly cookie |

**Acceptance criteria:**
- Users can authenticate and receive JWT
- JWT signing key loaded from Vault, not env var
- No secrets visible in `kubectl get secret -o yaml`
- Frontend login flow works end-to-end

### Exit Criteria for Phase 0
- [ ] All 7 P0 items resolved
- [ ] System boots with durable persistence by default
- [ ] At least one LLM provider returns real AI-generated responses
- [ ] At least one user can log in and execute a command
- [ ] No plain-text secrets in config or source control

---

## Phase 1: Production Foundation (Weeks 3-8)

**Goal:** Resolve all P1 gaps. Make the system production-capable.

### Sprint 1.1 — Core Test Coverage (2 weeks)

| Task | Owner | Deliverable |
|------|-------|-------------|
| Unit tests for Orchestrator, Runtime | Backend | ≥80% coverage on execution pipeline |
| Unit tests for Policy, Governance | Backend | ≥80% coverage on governance pipeline |
| Unit tests for Memory, Reasoner, ModelRouter | Backend | ≥80% coverage on AI pipeline |
| Integration tests with Testcontainers | Backend | PostgreSQL + NATS integration test project |
| Connector integration sandboxes | Backend | Sandbox tests for Salesforce, HubSpot |

**Acceptance criteria:**
- `dotnet test` passes with ≥80% line coverage on core engines
- Integration tests run against real PostgreSQL and NATS (via Testcontainers)
- CI pipeline includes coverage gates

### Sprint 1.2 — State Persistence (1 week)

| Task | Owner | Deliverable |
|------|-------|-------------|
| Persist workflow state | Backend | PostgreSQL-backed WorkflowEngine |
| Persist agent identity | Backend | PostgreSQL-backed AgentIdentityStore |
| Persist governance execution state | Backend | Redis for ephemeral counts + PostgreSQL for audit |
| Implement distributed locking | Backend | Redlock for task dedup and leader election |

**Acceptance criteria:**
- Active workflows survive pod restart
- Agent profiles persist across restarts
- No duplicate task execution across multiple runtime instances

### Sprint 1.3 — Observability & API Docs (1 week)

| Task | Owner | Deliverable |
|------|-------|-------------|
| OpenAPI specification | Backend | Swashbuckle annotations + Swagger UI |
| Centralized logging | DevOps | Serilog → OpenTelemetry → Loki |
| Deep health checks | Backend | Health endpoint validates PostgreSQL + NATS connectivity |
| Real embedding model | Backend | OpenAI/Cohere embedding integration in MemoryStore |

**Acceptance criteria:**
- Swagger UI accessible at `/swagger`
- Logs searchable in Loki/Grafana
- Health check returns `unhealthy` when DB is down
- Semantic search returns meaningfully ranked results

### Exit Criteria for Phase 1
- [ ] All 10 P1 items resolved
- [ ] ≥80% test coverage on core engines
- [ ] Integration tests pass in CI
- [ ] All state durable across restarts
- [ ] OpenAPI spec published
- [ ] Centralized logging operational

---

## Phase 2: Enterprise Hardening (Weeks 9-16)

**Goal:** Resolve P2 gaps. Make the system enterprise-capable.

### Sprint 2.1 — Multi-Tenant Isolation (2 weeks)

| Task | Owner | Deliverable |
|------|-------|-------------|
| Schema-per-tenant architecture | Backend | Tenant-scoped PostgreSQL schemas |
| Tenant provisioning API | Backend | `POST /admin/tenants` with schema creation |
| Per-tenant connection routing | Backend | TenantDbContextFactory |
| Row-level security policies | DBA | RLS policies on shared tables |
| Per-tenant encryption keys | DevOps | KMS key-per-tenant integration |

### Sprint 2.2 — Enterprise Auth (2 weeks)

| Task | Owner | Deliverable |
|------|-------|-------------|
| OIDC Authorization Code + PKCE | Backend | `/auth/oidc/callback` endpoint |
| SAML adapter | Backend | SAML 2.0 assertion consumer service |
| Per-tenant API keys | Backend | API key issuance, rotation, scoping |
| Frontend auth flows | Frontend | SSO redirect, session management, protected routes |
| MFA support | Backend | TOTP enrollment and verification |

### Sprint 2.3 — Observability & DR (2 weeks)

| Task | Owner | Deliverable |
|------|-------|-------------|
| Grafana dashboards | DevOps | Pre-built dashboards for API, agents, workflows, LLM |
| Alerting rules | DevOps | PagerDuty integration with escalation |
| SLO definitions | SRE | SLOs for API p99, task success rate, loop cycle time |
| Automated backup | DevOps | PostgreSQL backup with PITR; NATS stream replication |
| DR runbook | SRE | Recovery procedures with RTO/RPO targets |

### Sprint 2.4 — Testing & Security (2 weeks)

| Task | Owner | Deliverable |
|------|-------|-------------|
| E2E tests | QA | Playwright tests for critical user journeys |
| Load tests | QA | k6 tests for API throughput and intelligence loop |
| Security scanning | DevOps | Snyk + Trivy + CodeQL in CI pipeline |
| Connector webhooks | Backend | Webhook receiver with signature verification |
| Frontend login integration | Frontend | Complete auth UI with SSO + session refresh |

### Exit Criteria for Phase 2
- [ ] All 12 P2 items resolved
- [ ] Tenants isolated at database level
- [ ] SSO works with at least one IdP (Okta or Entra)
- [ ] Grafana dashboards showing live production metrics
- [ ] E2E tests cover onboarding, command, and dashboard flows
- [ ] Load tests establish baseline performance numbers
- [ ] Backup/restore tested successfully

---

## Phase 3: Scale & Polish (Weeks 17-22)

**Goal:** Resolve P3 items. Prepare for enterprise rollout.

### Sprint 3.1 — Infrastructure as Code (2 weeks)

| Task | Owner | Deliverable |
|------|-------|-------------|
| Terraform modules | DevOps | AWS/GCP modules for VPC, K8s, RDS, ElastiCache |
| ArgoCD GitOps | DevOps | dev → staging → production promotion |
| Environment parity | DevOps | Staging mirrors production topology |

### Sprint 3.2 — Polish & Docs (2 weeks)

| Task | Owner | Deliverable |
|------|-------|-------------|
| CLI implementation | Backend | System.CommandLine with ops/admin commands |
| Prompt template registry | Backend | Centralized prompt management |
| API versioning policy | Backend | Deprecation headers, migration guide |
| Developer documentation | Docs | Setup guide, contribution guide, coding standards |
| Security documentation | Docs | Security model, threat model, compliance matrix |
| Operational runbooks | SRE | Incident response procedures |

### Sprint 3.3 — Compliance & Cleanup (2 weeks)

| Task | Owner | Deliverable |
|------|-------|-------------|
| WCAG accessibility audit | Frontend | WCAG 2.1 AA compliance |
| PII detection | Backend | Content filtering on LLM I/O |
| Cost attribution | Backend | Per-tenant LLM cost tracking |
| Remove dead projects | Backend | Delete empty Planner, EventBus projects |
| Plugin system | Backend | Assembly-based plugin loading (if needed) |

### Exit Criteria for Phase 3
- [ ] Infrastructure provisioned via Terraform
- [ ] GitOps deployment pipeline operational
- [ ] Full documentation package complete
- [ ] Accessibility audit passed
- [ ] Enterprise compliance checklist completed

---

## Dependency Graph

```
Phase 0 (Truth Lock)
├── P0-007: Default persistence ──┐
├── P0-005: DB migrations ────────┤
├── P0-002: Audit persistence ────┤── Sprint 0.1 (PostgreSQL required first)
├── P0-003: RBAC persistence ─────┘
├── P0-006: Real LLM providers ──┐
├── P0-001: User auth ────────────┤── Sprint 0.2 (LLM + auth + secrets)
└── P0-004: Secrets management ───┘

Phase 1 (Production Foundation)
├── P1-001: Core tests ───────────── Sprint 1.1 (independent)
├── P1-002: Integration tests ────── Sprint 1.1 (needs Testcontainers)
├── P1-003: Workflow persistence ──┐
├── P1-005: Agent ID persistence ──┤── Sprint 1.2 (needs Phase 0 DB)
├── P1-004: Governance state ──────┤
├── P1-009: Distributed locking ───┘
├── P1-006: OpenAPI spec ─────────┐
├── P1-007: Centralized logging ──┤── Sprint 1.3 (independent)
├── P1-008: Deep health checks ───┤
└── P1-010: Real embeddings ──────┘

Phase 2 (Enterprise Hardening)
├── P2-001: Multi-tenant DB ──────── Sprint 2.1 (needs Phase 1 persistence)
├── P2-002: SSO/OIDC ────────────┐
├── P2-008: API keys ────────────┤── Sprint 2.2 (needs Phase 0 auth)
├── P2-010: Frontend auth ───────┘
├── P2-004: Grafana dashboards ──┐
├── P2-009: Backup/DR ───────────┤── Sprint 2.3 (needs Phase 1 observability)
├── P2-003: E2E tests ──────────┐
├── P2-011: Load tests ─────────┤── Sprint 2.4 (needs Phase 1 tests)
├── P2-012: Security scanning ──┤
└── P2-007: Connector webhooks ──┘
```

---

## Risk Register

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Schema-per-tenant migration breaks existing data | Medium | High | Run migration against copy of production data first |
| OIDC integration complexity with customer IdPs | High | Medium | Start with one IdP (Okta); add others incrementally |
| LLM API costs during testing | Medium | Low | Use cached responses for integration tests; budget cap |
| Performance regression from persistence overhead | Low | Medium | Benchmark before/after; optimize queries |
| Team bandwidth for 37 items | High | High | Prioritize P0 → P1 strictly; defer P3 if needed |

---

## Immediate Next Steps (This Week)

1. **Flip persistence defaults** — Make PostgreSQL required; add `--dev-mode` for in-memory
2. **Add DbUp migration project** — Version the schema from day one
3. **Persist audit log** — Highest-risk truth gap; small effort, huge credibility gain
4. **Persist RBAC state** — Roles must survive restarts
5. **Start JWT token issuance** — Cannot demo the system without user login

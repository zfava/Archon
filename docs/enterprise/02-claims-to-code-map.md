# 02 - Claims-to-Code Truth Map

> Maps marketing/architecture claims to actual code support.
> Audit date: 2026-03-17

---

## What ArchonAI CAN Truthfully Claim Today

### Claim: "AI-powered operations platform with autonomous intelligence loop"
**Verdict: TRUE**
- `IntelligenceLoopOrchestrator` implements a complete 8-phase cycle: Perception → Goal Generation → Reasoning → Simulation → Task Graph → Execution → Outcome Evaluation → Learning
- Runs as a `BackgroundService` with configurable interval
- Each phase calls real engine implementations, not stubs
- Evidence: `archonai/src/ArchonAI.IntelligenceLoop/IntelligenceLoopOrchestrator.cs` (261+ lines)

### Claim: "Multi-agent system with domain-specific agents"
**Verdict: TRUE**
- 5 domain agents implemented: Sales, Finance, Marketing, Operations, Support
- Each has a dedicated engine with real business logic (pipeline analysis, anomaly detection, campaign ROI, etc.)
- Base `ToolEnabledAgent` supports LLM generation and tool execution
- Agent supervisor with health monitoring, heartbeat tracking, runaway loop detection
- Evidence: `archonai/src/ArchonAI.Agents.*/` (4 files each, 300-400 lines of business logic per engine)

### Claim: "Enterprise connector ecosystem"
**Verdict: TRUE**
- 10 connectors with real HTTP integrations: Salesforce, HubSpot, QuickBooks, Slack, Google Workspace, Microsoft 365, CRM, ERP, Financial, Messaging
- Salesforce connector has full OAuth 2.0 password flow, SOQL queries, record CRUD
- All connectors use HttpClientFactory, retry logic, rate limit handling
- Test coverage with MockHttpMessageHandler for all connectors
- Evidence: `archonai/src/ArchonAI.Connectors/` (17 source files)

### Claim: "Governance and policy enforcement"
**Verdict: TRUE**
- PolicyEngine: multi-factor risk scoring (forbidden capabilities, input validation, high-risk detection, confidence thresholds)
- GovernanceKernel: 6-stage execution validation with performance gates, resource quotas, security checks
- SecurityPolicyEngine for runtime security evaluation
- Approval workflow state machine (not-required → pending → approved → checkpoint → overridden)
- RBAC with granular permissions (3 system roles, 9 permission categories)
- Evidence: `archonai/src/ArchonAI.Policy/PolicyEngine.cs`, `archonai/src/ArchonAI.Governance/GovernanceKernel.cs`

### Claim: "Adaptive model routing"
**Verdict: TRUE (routing logic) / FALSE (actual LLM calls)**
- ModelRouter with strategy-based selection (cost, latency, quality optimization)
- AdaptiveRoutingWeightEngine with performance-weighted model selection
- ModelPerformanceTracker with composite scoring across 4 dimensions
- Fallback chain support across providers
- **Critical caveat:** The routing logic is production-real, but ALL 4 model providers (OpenAI, Anthropic, Azure, Local) are echo-back stubs that return `$"[provider:model] {prompt}"` without making HTTP calls
- Evidence: `archonai/src/ArchonAI.ModelRouter/` (5 files), `archonai/src/ArchonAI.Models/` (4 providers, all mocked)

### Claim: "Knowledge graph and organizational memory"
**Verdict: TRUE**
- OrganizationalMemoryStore with semantic search (embeddings + recency + importance + graph boost)
- PostgresKnowledgeGraphStore with real SQL persistence
- KnowledgeGraphEngine for hierarchical relationship management
- Character-trigram embedding generation
- Evidence: `archonai/src/ArchonAI.Memory/`, `archonai/src/ArchonAI.Knowledge/`, `archonai/src/ArchonAI.KnowledgeGraph/`

### Claim: "Workflow orchestration with deterministic state machine"
**Verdict: TRUE**
- WorkflowEngine: Created → Planning → Scheduled → Executing → Evaluating → Completed/Failed → Escalated
- DistributedTaskOrchestrator with priority queues and governance-integrated execution
- WorkflowDesigner with cycle detection, structural validation, and simulation
- Evidence: `archonai/src/ArchonAI.Workflow/`, `archonai/src/ArchonAI.Orchestrator/`

### Claim: "Production CI/CD with containerized deployment"
**Verdict: TRUE**
- GitHub Actions pipeline: build → test → Docker → publish
- 6 Dockerfiles with multi-stage .NET 10 builds
- Helm chart with proper resource limits, health probes, environment config
- docker-compose.yml for local development stack
- Evidence: `.github/workflows/ci-cd.yml`, `archonai/deploy/`

### Claim: "Real-time dashboards via WebSocket"
**Verdict: TRUE**
- SignalR hub at `/hubs/control-plane-dashboard`
- Streams: agent-activity, task-performance, system-health, alerts, model-usage
- Auto-reconnect with exponential backoff
- Evidence: `archonai-ui/src/features/activity/hooks/useDashboardHub.ts`

### Claim: "Explainability and attribution"
**Verdict: TRUE**
- ExplanationEngine generates factor breakdowns for strategy, agent, and decision explanations
- AttributionView traces outcomes through knowledge graph relationships
- Outcome evaluation with expected-vs-actual comparison
- Evidence: `archonai/src/ArchonAI.Reasoner/ExplanationEngine.cs`, `archonai-ui/src/features/attribution/`

---

## What ArchonAI CANNOT Truthfully Claim Today

### Claim: "AI-powered decisions and responses"
**Verdict: FALSE**
- All 4 LLM model providers (`OpenAiModelProvider`, `AnthropicModelProvider`, `AzureOpenAiModelProvider`, `LocalModelProvider`) are echo-back stubs
- They return `$"[provider:model] {prompt}"` — the prompt is echoed, not processed by any LLM
- `HttpClient` is registered in DI but **never used** by any model provider
- `appsettings.json` defines API keys and endpoints but they are **never read** by the providers
- The entire intelligence loop, agent execution, and reasoning pipeline runs, but produces no actual AI-generated output
- ReasoningEngine is rule-based only (if failures → "safe-mode", else → "standard")
- **Gap:** This is the single largest truth gap in the codebase. The platform orchestrates real workflows but produces no AI intelligence.

### Claim: "Enterprise-grade authentication and SSO"
**Verdict: FALSE**
- JWT infrastructure exists (signing key validation, bearer token middleware) but **no token issuance endpoint**
- No user login flow — no `/auth/login`, no `/auth/register`, no `/auth/refresh`
- No OIDC/SAML integration with enterprise IdPs (Okta, Entra, Auth0)
- No session management, no MFA
- RBAC roles/assignments stored in-memory — lost on restart
- Frontend has no login page, no auth context, no token management
- **Gap:** The system authenticates requests but has no way to create authenticated sessions

### Claim: "Multi-tenant isolation"
**Verdict: FALSE (decorator-only)**
- MultiTenantContext uses `AsyncLocal<string>` for tenant scoping
- TenantMemoryStore prefixes keys with `tenant:{id}:` — this is string prefixing, not isolation
- All tenants share the same ConcurrentDictionary (or same PostgreSQL schema with no row-level security)
- No tenant provisioning API
- No per-tenant encryption keys, no per-tenant connection strings, no schema-per-tenant
- **Gap:** A bug in key construction could leak data between tenants

### Claim: "Persistent, durable state"
**Verdict: FALSE (by default)**
- System defaults to in-memory for everything unless explicitly configured
- Audit log (with SHA256 integrity chain) is in ConcurrentDictionary — **lost on restart**
- RBAC state is in ConcurrentDictionary — **lost on restart**
- Governance execution tracking is in ConcurrentDictionary — **lost on restart**
- Agent identity profiles are in ConcurrentDictionary — **lost on restart**
- Workflow state is in-memory — **lost on restart**
- PostgreSQL and NATS are available but require manual configuration
- **Gap:** No data survives a pod restart in default configuration

### Claim: "Immutable audit trail"
**Verdict: PARTIALLY FALSE**
- AuditLogService has proper SHA256 checksum chaining and integrity verification
- **But the entire chain is stored in ConcurrentDictionary** — wiped on restart
- No database persistence for audit records
- Cannot satisfy compliance requirements (SOC2, HIPAA, SOX) without durable storage
- **Gap:** The integrity algorithm is real; the storage is not

### Claim: "Production-grade observability"
**Verdict: PARTIALLY FALSE**
- OpenTelemetry tracing and Prometheus metrics are configured
- Serilog structured logging exists
- **Missing:** No Grafana dashboards, no alerting rules, no SLO definitions, no centralized log aggregation, no distributed tracing visualization, no incident runbooks
- **Gap:** Telemetry is emitted but not consumed by any monitoring stack

### Claim: "Database migrations and schema management"
**Verdict: FALSE**
- PostgresMemoryRecordRepository uses `CREATE TABLE IF NOT EXISTS` — no versioned migrations
- PostgresKnowledgeGraphStore bootstraps schema on first use
- No EF Core migrations, no Flyway, no DbUp
- **Gap:** Schema changes require manual intervention; no rollback capability

### Claim: "Secrets management"
**Verdict: FALSE**
- JWT signing key: `Environment.GetEnvironmentVariable("ARCHONAI_JWT_SIGNING_KEY")` or appsettings
- Connector credentials (Salesforce OAuth, etc.) via `IOptions<T>` from config
- Helm values.yaml: `jwtSigningKey: change-me-please-use-at-least-32-characters`
- PostgreSQL password in plain text in values.yaml
- **Gap:** No Vault, no KMS, no encrypted secrets, no rotation

### Claim: "Distributed, scalable architecture"
**Verdict: PARTIALLY TRUE**
- Architecture is designed for distribution (separate worker processes, NATS bus, cluster coordinator)
- But cluster coordination is in-memory (ConcurrentDictionary node registry)
- No distributed locking (no Redis/etcd/ZooKeeper for coordination)
- No partitioned state management
- **Gap:** Multiple instances would have inconsistent state

### Claim: "Comprehensive test coverage"
**Verdict: FALSE**
- 25 test files across 7 test projects — but only unit tests
- Tests cover agents and connectors only
- **No tests for:** Orchestrator, Runtime, Policy, Governance, Memory, IntelligenceLoop, Workflow, ModelRouter, Reasoner
- No integration tests, no E2E tests, no load tests, no frontend tests
- **Gap:** Core execution pipeline is completely untested

---

## What Must Change for Enterprise-Tier Claims

| Claim | Required Change | Effort |
|-------|----------------|--------|
| Enterprise auth | Implement OIDC/SAML integration, token issuance, session management, MFA | Large |
| Multi-tenant isolation | Database-per-tenant or RLS, per-tenant encryption, tenant provisioning API | Large |
| Persistent state | Make PostgreSQL + NATS the default; persist audit, RBAC, governance state | Medium |
| Immutable audit | Persist audit chain to PostgreSQL with write-once semantics | Small |
| Observability | Deploy Grafana stack, define SLOs, create alerting rules and dashboards | Medium |
| Schema migrations | Adopt DbUp or EF Core migrations; version all schema changes | Small |
| Secrets management | Integrate Vault or cloud KMS; remove plaintext secrets from config | Medium |
| Distributed coordination | Replace in-memory cluster state with Redis/etcd-backed coordination | Medium |
| Test coverage | Add core engine tests, integration tests, E2E tests, load tests | Large |
| API documentation | Generate OpenAPI spec from controllers; publish Swagger UI | Small |

# 01 - Product Truth Audit

> Full subsystem-by-subsystem classification of ArchonAI codebase maturity.
> Audit date: 2026-03-17

## Classification Legend

| Label | Meaning |
|-------|---------|
| **production-real** | Fully implemented with real business logic and external integrations |
| **partially-real** | Core logic exists but missing persistence, external integration, or error handling |
| **in-memory/prototype** | Functional logic but all state in ConcurrentDictionary; lost on restart |
| **placeholder/scaffold** | Project exists but contains no meaningful implementation |
| **missing** | Expected by architecture but no code exists |

---

## 1. API Layer

### ArchonAI.Api — production-real
- 11 source files: Program.cs, AuditLogService, JwtOptions, RbacService, ObservabilityService, TracingAgentExecutor, WorkflowDesignService, etc.
- Real JWT Bearer authentication with configurable signing keys
- Rate limiting with tiered policies (standard 120/min, admin 60/min, connectors 1000/min)
- RBAC with granular permissions (agents:read/write/execute, workflows, connectors, admin, policy, monitoring)
- Three system roles: Admin, Operator, Viewer
- OpenTelemetry tracing + Prometheus metrics
- **Caveat:** AuditLogService stores audit chain in `ConcurrentDictionary` (in-memory). ObservabilityService.GetWorkflowPerformanceAsync() is stubbed.

### ArchonAI.Gateway — production-real
- 4 source files: Program.cs, CorrelationIdMiddleware, GatewayMetrics, GatewayRequestMiddleware
- Real ASP.NET Core JWT Bearer authentication
- YARP reverse proxy to backend services
- Correlation ID propagation
- Rate limiting enforcement
- Serilog structured logging

### ArchonAI.AdminAPI — partially-real
- 3 source files: AdminService, AdminOptions, DependencyInjection
- Agent/workflow management, policy configuration, monitoring snapshots
- **Caveat:** Workflows stored in-memory. TotalTasksExecuted/TotalTasksFailed always return 0.

---

## 2. Auth / Identity

### ArchonAI.Identity — in-memory/prototype
- 3 source files: InMemoryAgentIdentityStore, IdentityOptions, DependencyInjection
- Tracks agent capabilities, permissions, execution history
- All storage in `ConcurrentDictionary<Guid, AgentIdentityProfile>`
- **No user identity.** This is agent identity only.
- **Missing:** User authentication flow, login endpoint, token issuance, SSO/OIDC integration, session management

### ArchonAI.Api (RBAC) — production-real (logic), in-memory (state)
- RbacService implements complete role/assignment/policy evaluation
- Access decision evaluation with policy matching
- **State in ConcurrentDictionary** — roles/assignments lost on restart

---

## 3. Tenancy

### ArchonAI.MultiTenant — in-memory/prototype
- 6 source files: MultiTenantContext, TenantMemoryStore, TenantResourceGovernor, TenantStrategyStore, etc.
- Tenant isolation via `AsyncLocal<string>` scoping + key prefixing
- Resource quota enforcement per tenant
- **Decorator pattern** over underlying stores — no database-level tenant isolation
- Delegates all persistence to wrapped `IMemoryStore` (in-memory by default)

---

## 4. Authorization / Policy / Governance

### ArchonAI.Policy — production-real
- PolicyEngine: multi-factor evaluation with forbidden capabilities, risk scoring, approval workflows
- Computes risk scores (0-100), determines approval state
- Configuration-driven rules
- No external dependencies (pure evaluation logic)

### ArchonAI.Governance — production-real
- GovernanceKernel: 6-stage execution validation
  1. Identity validation
  2. Capability validation
  3. Performance-based gating (success rate thresholds)
  4. Concurrent execution limits per agent
  5. Security policy evaluation
  6. Policy engine evaluation
- Audits all decisions via IEventBus
- **Tracks active executions in ConcurrentDictionary** (in-memory)

### ArchonAI.Sandbox — partially-real
- AgentSandboxManager: real constraint validation (memory, CPU, network, API permissions)
- State tracking in-memory

---

## 5. AI Execution

### ArchonAI.ModelRouter — production-real
- Adaptive weight-based routing across LLM providers (OpenAI, Anthropic, Azure, local)
- Performance tracking: success rate, latency, cost, accuracy
- Composite scoring: `successRate * 0.35 + accuracy * 0.30 + latencyScore * 0.20 + costScore * 0.15`
- Fallback chain support
- **Caveat:** No integration tests proving real LLM API calls work end-to-end

### ArchonAI.Agents (Base) — production-real
- ToolEnabledAgent base class with real model generation and tool execution
- Integrates with model routing and tool framework

### ArchonAI.Agents.Sales — production-real
- SalesEngine: pipeline analysis, opportunity prioritization, outreach recommendations
- Multi-factor scoring: `(valueScore * 0.4) + (stageScore * 0.35) + (recencyScore * 0.25)`
- Real DataFabric queries

### ArchonAI.Agents.Finance — production-real
- FinanceEngine: financial analysis, anomaly detection (z-score), budget workflows
- Real DataFabric queries

### ArchonAI.Agents.Marketing — production-real
- MarketingEngine: campaign analysis, ROI calculation, channel performance
- Real DataFabric queries

### ArchonAI.Agents.Operations — production-real
- OperationsEngine: workflow analysis, inefficiency detection, multi-agent reasoning
- Real DataFabric queries

### ArchonAI.Agents.Support — production-real
- SupportEngine: ticket analysis, recurring issue detection, auto-response recommendations
- Real DataFabric queries

---

## 6. Workflow / Runtime

### ArchonAI.Orchestrator — production-real
- DistributedTaskOrchestrator: priority queues, governance validation, retry with exponential backoff
- Rate limiting with window-based dispatch throttling

### ArchonAI.IntelligenceLoop — production-real
- 8-phase autonomous cycle: Perception → Goal Generation → Reasoning → Simulation → Task Graph → Execution → Evaluation → Learning
- Runs as IHostedService with configurable interval

### ArchonAI.Runtime — production-real
- AgentRuntime: agent registration, task scheduling, execution orchestration
- Full supervision, sandbox management, governance integration

### ArchonAI.TaskRuntime — production-real
- TaskExecutionEngine: agent selection, sandbox isolation, governance validation, retry logic

### ArchonAI.Workflow — production-real
- Deterministic state machine: Created → Planning → Scheduled → Executing → Evaluating → Completed/Failed → Escalated
- Proper transition validation

### ArchonAI.WorkflowRuntime — production-real
- Task queue management, scheduler integration, state transitions

### ArchonAI.WorkflowDesigner — production-real
- Graph management, structural validation, cycle detection (DFS), simulation, export

### ArchonAI.Scheduler — production-real
- ResourceScheduler: task prioritization, GPU slot allocation, model routing

### ArchonAI.Supervisor — production-real
- Agent lifecycle management, health tracking, heartbeat monitoring, runaway loop detection

---

## 7. Control Plane

### ArchonAI.ControlPlane — production-real (assumed)
- Policy management, tenant management, configuration management
- Exposed via API endpoints

---

## 8. Connectors / Integrations

### ArchonAI.Connectors — production-real
- **10 connectors** with real HTTP integrations:
  - Salesforce: OAuth 2.0 password flow, SOQL queries, record CRUD, rate limiting
  - HubSpot: HTTP-based CRM operations
  - QuickBooks: Financial accounting integration
  - Slack: Messaging API
  - Google Workspace: G Suite integration
  - Microsoft 365: M365 API
  - CRM (generic): HTTP CRUD operations
  - ERP (generic): Enterprise resource planning
  - Financial (generic): Financial system integration
  - Messaging (generic): Messaging system integration
- HttpClientFactory pattern, retry logic, event publishing

---

## 9. Memory / Knowledge / Telemetry

### ArchonAI.Memory — production-real
- OrganizationalMemoryStore: semantic search with embedding-based retrieval
- Composite scoring: embeddings + recency + importance + graph boost
- Character-trigram embedding generation
- Knowledge graph relationship persistence

### ArchonAI.Knowledge — production-real (dual implementation)
- InMemoryKnowledgeGraphStore: ConcurrentDictionary-based
- PostgresKnowledgeGraphStore: Real SQL with jsonb, proper indexing
- **Conditional wiring by configuration**

### ArchonAI.KnowledgeGraph — production-real
- Relationship orchestration: agent → task → workflow → organization → system → datasource

### ArchonAI.Telemetry — production-real
- SystemInsightEngine: bottleneck detection, anomaly detection (std deviation), P95/P99 latency

### ArchonAI.Trace — production-real
- Observability/tracing infrastructure

---

## 10. Infrastructure / Persistence

### ArchonAI.Infrastructure — partially-real (conditional)
- **Memory persistence:** PostgresMemoryRecordRepository (real Npgsql, pgvector, HNSW indexing) OR InMemoryStore
- **Event bus:** NatsEventBus (real NATS with retry/DLQ) OR InMemoryEventBus
- **Conditional wiring in DI:**
  ```csharp
  // If ConnectionString empty → in-memory; if set → PostgreSQL
  // If UseNats=false → in-memory; if UseNats=true → NATS
  ```
- **Cluster coordination:** In-memory node registry (ConcurrentDictionary)
- **Schema bootstrap:** `CREATE TABLE IF NOT EXISTS` — no migration framework

---

## 11. Frontend App Shell

### archonai-ui — production-real
- React 19 + TypeScript + Vite
- react-router-dom with 11 routes
- Real API client (`/api/v1` via fetch)
- SignalR WebSocket for real-time dashboards
- **All 11 hooks call real backend API** — zero mock data generators
- **No auth UI** — no login page, no token management, no session handling
- **No global state management** — hooks-based local state only

---

## 12. Observability / SRE

### OpenTelemetry — partially-real
- Tracing configured in API and Gateway
- Prometheus metrics exporter
- **Missing:** Grafana dashboards, alerting rules, SLO definitions, runbooks

### Logging — production-real
- Serilog structured logging to console
- **Missing:** Centralized log aggregation (Loki/ELK)

---

## 13. Deployment / Ops

### Docker — production-real
- 6 Dockerfiles: API, CLI, Runtime, Scheduler, Agents, Gateway
- Multi-stage builds with .NET 10 SDK
- docker-compose.yml for local stack (API + PostgreSQL/pgvector + NATS)

### Kubernetes — production-real
- Raw manifests: namespace, deployments, services, configmaps, secrets
- Helm chart with values.yaml for all services
- Health/readiness probes configured
- Resource requests/limits defined

### CI/CD — production-real
- GitHub Actions: build → test → Docker build/push → publish artifacts
- GHCR container registry
- Helm chart and K8s manifest artifacts

### **Missing:**
- Terraform / IaC for cloud infrastructure
- ArgoCD / GitOps
- Staging/production environment separation
- Blue-green or canary deployment strategy
- Backup/restore procedures

---

## 14. Testing / Verification

### Unit Tests — partially-real
- 25 test files across 7 test projects
- Covers: all 5 domain agents, all 6 connectors (with MockHttpMessageHandler), WorkflowDesigner, AdminAPI
- Uses xUnit + NSubstitute
- **Missing:** Core engine tests (Orchestrator, Runtime, Policy, Governance, Memory, IntelligenceLoop)

### Integration Tests — missing
- No integration test project
- No tests against real PostgreSQL, NATS, or external APIs

### E2E Tests — missing
- No end-to-end test framework
- No frontend tests (no Playwright, Cypress, etc.)

### Load/Performance Tests — missing
- No load testing framework
- No performance benchmarks

---

## 15. Documentation

### Architecture — production-real
- ARCHITECTURE.md: comprehensive C4-style architecture description
- README.md: infrastructure overview with run instructions

### API Docs — missing
- No OpenAPI/Swagger specification
- No generated API documentation

### Runbooks — missing
- No operational runbooks
- No incident response procedures

---

## 16. Placeholder / Empty Projects

| Project | Status |
|---------|--------|
| ArchonAI.Planner | **Empty** — no .cs files |
| ArchonAI.EventBus | **Empty** — no .cs files; functionality in Infrastructure |
| ArchonAI.Cli | **Scaffold** — `Console.WriteLine("ArchonAI CLI")` only |
| ArchonAI.Plugins | **Scaffold** — PluginOptions only, no plugin loading |

---

## 17. Simulation Subsystems (Intentionally In-Memory)

| Project | Purpose | Classification |
|---------|---------|----------------|
| ArchonAI.Simulation | Workflow simulation with predictive models | in-memory/prototype |
| ArchonAI.WorkflowSimulation | Strategy simulation engine | in-memory/prototype |
| ArchonAI.Strategy | In-memory strategy store with config seeds | in-memory/prototype |

These are **by design** simulation/prediction engines and do not require persistent external storage.

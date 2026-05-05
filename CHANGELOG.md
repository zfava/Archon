# Changelog

All notable changes to the ArchonAI platform are documented in this file.

## [0.12.1] - 2026-05-05

### Removed
- Removed 6 empty stub projects (Planner, EventBus, Worker.Agents, Worker.Runtime, Worker.Scheduler, Cli) to reduce surface area
- Removed associated Dockerfiles (Dockerfile.agents, Dockerfile.runtime, Dockerfile.scheduler, Dockerfile.cli)
- Removed Kubernetes deployment manifests for removed worker services (agents, runtime, scheduler)
- Removed Helm chart templates and values entries for removed worker services
- Cleaned PodDisruptionBudget and NetworkPolicy templates of worker references

## [0.12.0] - 2026-03-23 (Archon16)

### Added
- Production readiness report (7/8 checks passing)
- Flaky test remediation (DurableWorkflowTests synchronous write fix)
- R23-R27 risk closures (SOQL injection, sync-over-async, empty projects, ISecretProvider async, frontend tests)
- 31 frontend contract tests across 4 test files
- Design quality uplift with DM Sans/JetBrains Mono typography and deep navy color palette
- API error handling hardening with normalized ApiError class and useApiCall hook

### Fixed
- Sync-over-async in ControlPlaneService and MemoryCompressionEngine
- Removed empty projects (EventBus, Planner) from solution
- SOQL injection surface in SalesforceConnector
- CI pipeline NETSDK1004 bug (missing dotnet restore before per-project test loops)

## [0.11.0] - 2026-03-22 (Archon10)

### Added
- 6 enterprise industry templates (manufacturing, healthcare, financial services, energy, defense, professional services)
- 48 named agents, 24 workflows, 18 shadow scenarios across verticals
- ComplianceBanner for all 6 industries
- Industry-specific KPI endpoint
- SignalR InspectionHub for real-time governance streaming
- Action safety auto-classification (5 categories, 24 tests)
- External Governance API (ExternalGovernanceEndpoints)
- Competitive differentiation documentation
- Connector maintenance runbook

### Removed
- Pool service, pest control, landscaping templates
- SaaS and e-commerce business types

## [0.10.0] - 2026-03-20 (Archon9)

### Added
- Event-driven inspection wiring via GovernanceEventSubscriber (8 event types)
- Deep-link query parameters across governed operation views
- Retry-from-step in workflow diagnostics
- Enterprise memory API documentation
- Proof analytics auto-emission
- Executive command inline metrics (ProofBrief, ActionSafetyBrief, WorkflowBrief)

## [0.9.0] - 2026-03-18 (Archon8)

### Added
- 9 identity-domain PostgreSQL stores (Users, Orgs, Membership, RefreshTokens, InviteTokens, MFA, TenantAuthConfig, ExternalIdentityLinks, OidcLoginSessions)
- Three vault providers (HashiCorp AppRole, AWS Secrets Manager, Azure Key Vault) with rotation detection
- CORS adversarial testing (12 vectors)
- 5 post-elite feature systems (Hero Workflows, Policy Simulation, Proof Analytics, Action Safety, Operator Inspection)
- Executive Command view
- Trust Lineage promoted to first-class navigation
- Governance demo endpoint (POST /demo/governance-loop)

## [0.8.0] - Earlier builds (Archon3-7)

### Foundation
- Core governance kernel with PolicyEngine, TrustTierService, GatedActionExecutor
- HMAC-signed manual override tokens with constant-time comparison
- SHA-256 hash-chained audit trails
- 6-tier trust model (observe-only through full policy envelope)
- Separation-of-duties enforcement
- OIDC federation with JIT provisioning
- TOTP/WebAuthn MFA with dedicated encryption
- DbUp migration framework (26 scripts with rollbacks)
- Polly circuit breakers for connector resilience
- 10 Grafana dashboards and Prometheus alerting
- Trivy container scanning and dependency vulnerability scanning
- Data retention automation and GDPR data subject rights
- 25 frontend feature modules
- React 19 frontend with TypeScript
- 6 enterprise connectors (Salesforce, HubSpot, QuickBooks, Slack, Google Workspace, Microsoft 365)
- Multi-model AI routing (OpenAI, Anthropic, Azure OpenAI, local Ollama)
- 452 HTTP endpoints across 17 endpoint files plus Gateway
- Digital twin with dependency mapping and bottleneck detection
- Strategy simulation with economic evaluation
- Workflow designer with visual DAG builder
- Agent registry with capability-based routing
- Cluster coordination with node management and load balancing

# Open Gaps After Supreme Phase

## Classification

Gaps are classified by impact level:

- **P0**: Would block an enterprise sale or fail a security audit
- **P1**: Would surface during technical due diligence or POC
- **P2**: Would improve operational maturity but not block deals

## P0 — Deal-Blocking Gaps

### 1. Database Migration Tooling

**Current state**: No schema migration framework. Tables/schemas are implied by in-memory service implementations.

**Gap**: When moving from in-memory to PostgreSQL persistence, there is no migration path. No Flyway, Liquibase, or EF Core migrations.

**Impact**: First production deployment will require manual schema creation. Schema changes between releases have no automated upgrade path.

**Recommended fix**: Add EF Core migrations or a lightweight SQL migration runner. Ship initial schema as a versioned migration.

### 2. Secret Rotation Automation

**Current state**: Secrets are injected at deploy time (inline or via External Secrets Operator). No rotation mechanism beyond re-deploying.

**Gap**: JWT signing keys and database credentials have no automated rotation. Key rotation requires a coordinated Helm upgrade + pod restart.

**Impact**: Compliance frameworks (SOC 2, ISO 27001) require evidence of secret rotation.

**Recommended fix**: Add ConfigMap hash annotation to pod templates for automatic rollout on Secret changes. Document rotation SOP. For ESO deployments, leverage ESO's built-in refresh interval.

### 3. Persistence Layer Implementation

**Current state**: All elite services use `ConcurrentDictionary`-backed in-memory storage. Data is lost on pod restart.

**Gap**: No durable persistence for decisions, exceptions, scenarios, outcomes, operational twin, memory, or trust tier policies.

**Impact**: Unacceptable for any environment beyond demo/POC. An enterprise buyer running a POC will lose all data on the first pod restart.

**Recommended fix**: Implement PostgreSQL-backed repositories for each service. The interfaces are already clean — swap the implementations behind the existing `I*Service` contracts.

## P1 — Due Diligence Gaps

### 4. Governance TenantId Type Inconsistency

**Current state**: `IGovernanceService` and `ITrustTierService` accept `string tenantId`. All other services accept `Guid tenantId`.

**Gap**: Creates friction in cross-service composition. The `ExecutiveCommandService` works around it with `.ToString()`, but this pattern is fragile.

**Impact**: Would be noticed during code review. Not functionally broken but signals early-stage architecture.

**Recommended fix**: Migrate governance and trust tier interfaces to `Guid tenantId`. Cascade through implementations and API endpoints.

### 5. Kubernetes RBAC and Service Accounts

**Current state**: No `ServiceAccount`, `Role`, or `RoleBinding` resources in Helm templates. Pods run with default service account.

**Gap**: Default service accounts may have broader permissions than necessary. No least-privilege enforcement at the Kubernetes layer.

**Impact**: Would fail a Kubernetes security audit. Private deployment customers will notice.

**Recommended fix**: Add per-service `ServiceAccount` resources with `automountServiceAccountToken: false` (except where needed for ESO).

### 6. Ingress Template

**Current state**: Gateway is exposed via `LoadBalancer` service type. No Ingress resource.

**Gap**: Private and single-tenant deployments expect an `Ingress` or `IngressRoute` resource for TLS termination, path routing, and integration with their existing ingress controller.

**Impact**: Customers must manually create Ingress resources.

**Recommended fix**: Add an optional Ingress template to Helm with annotations for common controllers (NGINX, ALB, Traefik).

### 7. Cross-Model Link Schema Standardization

**Current state**: Artifact links use slightly different field names across domains:
- Decision: `ArtifactType`, `ArtifactId`, `Description`
- Exception/Scenario: `ArtifactType`, `ArtifactId`, `Label`
- Memory: `EntityType`, `EntityId`, `Relationship`
- Twin: `ArtifactType`, `ArtifactId`, `Relationship`

**Gap**: No shared base type or interface for cross-model references.

**Impact**: Querying "all artifacts linked to decision X" requires domain-specific logic rather than a generic link query.

**Recommended fix**: Define a shared `IArtifactLink` interface or standardize field names. The semantic distinction between `ArtifactType` and `EntityType` is defensible but should be documented.

### 8. Rate Limiting on Elite Endpoints

**Current state**: Elite API groups (scenarios, exceptions, executive-command) inherit group-level rate limiting (`RequireRateLimiting("api")`).

**Gap**: The Executive Command endpoint fans out to 10 downstream queries. Under load, this is significantly more expensive than a typical endpoint. No endpoint-specific rate limiting.

**Impact**: A burst of executive summary requests could amplify into 10x the downstream load.

**Recommended fix**: Add a tighter rate limit for `/executive-command/summary` (e.g., 10 req/min vs 120 req/min for standard endpoints). Consider caching the summary with a short TTL (30-60s).

## P2 — Operational Maturity Gaps

### 9. Vault Agent Injector Support

**Current state**: `secrets.provider=vault-injector` is reserved in values.yaml but not implemented.

**Gap**: Customers using HashiCorp Vault without ESO cannot inject secrets via sidecar.

**Recommended fix**: Add vault annotation support to pod templates when `secrets.provider=vault-injector`.

### 10. Observability Integration for Elite Services

**Current state**: Elite services do not emit custom metrics or traces. OpenTelemetry is configured at the host level but elite service operations (exception scoring, scenario comparison, executive summary composition) are not instrumented.

**Gap**: Operations teams cannot monitor elite feature performance or identify slow composition paths.

**Recommended fix**: Add span instrumentation to `ExecutiveCommandService.GetCommandSummaryAsync` (one span per downstream read). Add counters for exception raises, scenario comparisons, and approval requests.

### 11. Audit Trail for Elite Operations

**Current state**: Elite services publish domain events via `IEventBus` but do not integrate with the audit log.

**Gap**: Exception status changes, scenario modifications, trust tier policy changes, and approval reviews are not recorded in the audit trail.

**Recommended fix**: Subscribe to elite domain events in the audit service and record structured audit entries.

### 12. Frontend Error Handling Consistency

**Current state**: Elite views catch API errors silently (`catch { /* ignore */ }`). Failed loads show a generic "Unable to load" message.

**Gap**: No distinction between network errors, authorization failures, and server errors. No retry mechanism.

**Recommended fix**: Add typed error handling with distinct UI states for loading, unauthorized, server error, and network error. Add a retry button for transient failures.

### 13. Pod Security Standards

**Current state**: All pods run as non-root with read-only root filesystem and drop ALL capabilities. Good baseline.

**Gap**: No `PodSecurityStandard` or `PodSecurityPolicy` resources. No `seccompProfile` or `appArmorProfile` annotations.

**Recommended fix**: Add `seccompProfile: RuntimeDefault` to pod security contexts. Consider adding `PodSecurityAdmission` labels to the namespace.

### 14. Health Check Integration for Elite Services

**Current state**: Health check endpoint (`/api/v1/health`) exists but does not probe elite service dependencies (e.g., can the executive command service reach all 6 downstream services?).

**Gap**: A degraded elite subsystem would not be detected by the health check.

**Recommended fix**: Add a readiness probe that validates elite service composition (lightweight ping to each dependency).

## Summary

| Priority | Count | Theme |
|---|---|---|
| P0 | 3 | Persistence, migrations, secret rotation |
| P1 | 5 | Type consistency, K8s RBAC, ingress, rate limiting, link schema |
| P2 | 6 | Vault injector, observability, audit trail, error handling, pod security, health checks |

The P0 gaps are the minimum work required before a production enterprise deployment. P1 gaps should be addressed before technical due diligence. P2 gaps represent operational maturity improvements.

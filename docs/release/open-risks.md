# Open Risks

## Purpose

This document enumerates residual risks in the ArchonAI release candidate, categorized by severity and assigned to either pre-release fix or post-release roadmap.

---

## Risk Register

### P0 — Must Address Before Production

| # | Risk | Category | Description | Mitigation | Owner |
|---|---|---|---|---|---|
| R1 | **AI execution produces no real output** | AI Execution | All 4 model providers fall back to echo stubs without API keys. Intelligence loop runs but produces fake reasoning. | Provide API keys at deployment. Document that echo responses are explicitly labeled (`FinishReason: echo_fallback`). | Platform team |
| R2 | **Core state lost on restart** | Persistence | RBAC assignments, audit entries, governance decisions, agent registrations, and approval states are stored in `ConcurrentDictionary`. All data is lost on process restart. | Implement PostgreSQL-backed stores for all core services. Migration scripts for schema versioning. | Platform team |
| R3 | **Secrets in plaintext configuration** | Security | JWT signing keys, database passwords, and API keys stored in `appsettings.json` and Helm `values.yaml`. Exposed in source control and container images. | Integrate HashiCorp Vault or Kubernetes external secrets operator. Rotate signing keys. | Security team |
| R4 | **No SSO/OIDC integration** | Identity | JWT issuance works but there is no enterprise identity provider integration. Users must register directly. | Implement OIDC middleware for Okta/Entra/Auth0. | Identity team |

### P1 — Should Address Before Enterprise Pilot

| # | Risk | Category | Description | Mitigation | Owner |
|---|---|---|---|---|---|
| R5 | **No MFA support** | Identity | Single-factor authentication only. Compliance blocker for regulated industries (healthcare, finance). | Add TOTP or FIDO2 MFA flow. | Identity team |
| R6 | **No database migration framework** | Persistence | Schema management uses `CREATE TABLE IF NOT EXISTS`. No version tracking, rollback capability, or schema evolution path. | Adopt Entity Framework migrations or DbUp. | Platform team |
| R7 | **No load/performance testing** | Reliability | Zero evidence of system behavior under concurrent load. Resource limits configured but never validated. | Implement k6 or NBomber load tests. Establish SLA baselines. | QA team |
| R8 | **Worker services have no health endpoints** | Observability | Runtime, Scheduler, and Agents workers expose no `/health` endpoint. Kubernetes cannot detect unhealthy workers. | Add minimal health endpoint to each worker service. | Platform team |
| R9 | **No circuit breaker in connector layer** | Reliability | Retry logic exists, but sustained failures trigger unlimited retries until max retries exhausted. No circuit breaker to prevent cascading failures. | Integrate Polly circuit breaker policy. | Connector team |
| R10 | **Connector health metrics always report 0** | Observability | `System.Diagnostics.Metrics.Counter<long>` has no in-process read API. ObservabilityService reports 0 for all connector operations. Real values exported only via Prometheus. | Implement shadow counters for connector operations (as already done for task execution), or query Prometheus directly. | Observability team |

### P2 — Post-Release Roadmap

| # | Risk | Category | Description | Mitigation | Owner |
|---|---|---|---|---|---|
| R11 | **No container security scanning** | Security | No automated CVE checking in CI/CD pipeline for Docker base images or application dependencies. | Add Trivy or Snyk to CI pipeline. | DevOps team |
| R12 | **No dependency vulnerability scanning** | Security | No `dotnet list package --vulnerable` integration. Known-CVE risk in transitive NuGet packages. | Add `dotnet list package --vulnerable` to CI. | DevOps team |
| R13 | **CORS not tested under adversarial conditions** | Security | CORS configuration exists in Gateway but not validated with browser-context attacks. | Add browser-context integration tests. | Security team |
| R14 | **Generic connectors are stubs** | Connectors | CRM, ERP, Financial, and Messaging connectors return deterministic HTTP responses. Not connected to real APIs. | Implement real integrations or clearly mark as "template connectors" in documentation. | Connector team |
| R15 | **No Grafana dashboards or alerting** | Observability | Prometheus metrics are exported but no pre-built dashboards or alert rules exist. | Create standard operational dashboards and alert rules. | SRE team |
| R16 | **Database connection encryption not configured** | Security | PostgreSQL connection strings do not include `SslMode=Require`. Data in transit between services and database is unencrypted. | Add `SslMode=Require` to connection strings. Configure TLS certificates. | Platform team |
| R17 | **No data retention policies** | Compliance | Audit logs, traces, and execution history have no TTL or archival strategy. | Implement configurable retention with automatic archival. | Compliance team |
| R18 | **No GDPR/data subject access request flow** | Compliance | Tenant isolation exists but no mechanism for data subject access, portability, or right-to-erasure requests. | Implement data export and erasure APIs. | Compliance team |

---

## Risk Heat Map

```
              Low Impact    Medium Impact    High Impact    Critical Impact
            ┌─────────────┬──────────────┬──────────────┬──────────────┐
 Likely     │             │ R10, R15     │ R8           │ R1, R2       │
            │             │              │              │              │
 Possible   │ R13         │ R14, R17     │ R5, R7, R9   │ R3, R4       │
            │             │              │              │              │
 Unlikely   │             │ R11, R12, R18│ R6, R16      │              │
            └─────────────┴──────────────┴──────────────┴──────────────┘
```

---

## Risk Acceptance Criteria

For release candidate approval, the following conditions must be met:

1. **P0 risks documented and mitigated** — All P0 risks have documented workarounds or are blocked as known limitations.
2. **No silent data loss** — Echo stubs are explicitly labeled. In-memory state loss is documented.
3. **Security hardening complete** — Containers run as non-root, Kubernetes pods have security contexts, postgres credentials use Secrets.
4. **Zero build warnings, zero test failures** — Build produces 0 warnings, all 168 enterprise tests pass.

### Current Status

| Criterion | Status |
|---|---|
| P0 risks documented | **Met** — R1-R4 documented with mitigations |
| No silent data loss | **Met** — Echo responses labeled, in-memory state documented |
| Security hardening complete | **Met** — Non-root Dockerfiles, K8s security contexts, postgres Secrets |
| Zero warnings / failures | **Met** — 0 warnings, 168/168 tests pass |

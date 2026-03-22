# Deployment Readiness Summary

Last verified: 2026-03-20

## Purpose

This document maps deployment artifacts to runtime reality. It tells operators, buyers, and diligence reviewers what deployment modes are supported, what has been tested, and where gaps remain.

**Last Audited:** 2026-03-20
**Audit Method:** Helm chart inspection (v0.3.0), Dockerfile review, CI/CD pipeline review, Kubernetes manifest review, source-level verification.

---

## Deployment Modes

### A. Local Development (Docker Compose)

**Status: Source-Complete, Functional**

| Aspect | Detail |
|---|---|
| Artifact | `archonai/docker-compose.yml` |
| Services | 7: Gateway, API, Runtime, Scheduler, Agents, PostgreSQL (pgvector), NATS |
| Start command | `docker compose up --build` |
| Health checks | PostgreSQL readiness gated; service dependencies declared |
| Non-root containers | All 6 Dockerfiles use `USER archon` (uid 1654) |
| Secret handling | `.env` file (excluded from `.dockerignore` and images) |

**What works:** All services start, health checks pass, API endpoints respond, tests run.
**What doesn't work:** AI execution (no API keys), connector data (no credentials), SSO login (no IdP configured).

### B. Kubernetes — Shared SaaS

**Status: Source-Complete, Not Runtime-Validated**

| Aspect | Detail |
|---|---|
| Artifact | `deploy/helm/archonai/` with `values.yaml` or `values-saas.yaml` |
| Replicas | Gateway: 2, API: 2, Runtime: 2, Scheduler: 1, Agents: 3 |
| Isolation | Multi-tenant row-level isolation in shared PostgreSQL |
| Rate limiting | Standard: 120 req/min, Admin: 60 req/min, Connectors: 200 req/min |
| Network policies | Enabled — ingress/egress restricted per service |
| PodDisruptionBudgets | minAvailable=1 for Gateway, API, Agents |
| Secret provider | Default: inline K8s Secret. Supports: external-secrets, vault-injector |

**What's proven:** Helm templates render without errors. Security contexts, resource limits, probes, and network policies are structurally correct.
**What's not proven:** No deployment to a real Kubernetes cluster has been validated. No live traffic. No real PDB eviction test.

### C. Kubernetes — Single-Tenant Hosted

**Status: Source-Complete, Not Runtime-Validated**

| Aspect | Detail |
|---|---|
| Artifact | `deploy/helm/archonai/` with `values-single-tenant.yaml` |
| Namespace | Dedicated per customer: `archonai-<customer>` |
| Replicas | Reduced: Runtime 1, Agents 2 |
| Isolation | Infrastructure-level: dedicated PostgreSQL + NATS per customer |

**Same validation gap as SaaS mode.**

### D. Private/VPC Enterprise

**Status: Source-Complete, Not Runtime-Validated**

| Aspect | Detail |
|---|---|
| Artifact | `deploy/helm/archonai/` with `values-private.yaml` |
| Infrastructure | Customer-provided PostgreSQL, NATS, secret store |
| Container registry | Customer mirror (ECR/GCR/ACR/Harbor) |
| Secret provider | External vault integration (Helm templates exist) |

**Vault integration:** Three `ISecretProvider` implementations exist (HashiCorp Vault, AWS Secrets Manager, Azure Key Vault) and are registered in the `ChainedSecretProvider` chain. Helm templates support external-secrets operator and vault-injector sidecar. Application code reads secrets from the full chain: Vault → AWS → Azure → File → Environment.

---

## Container Security

| Control | Status | Evidence |
|---|---|---|
| Non-root execution | **Verified** | All 6 Dockerfiles: `USER archon` (uid 1654) |
| Multi-stage builds | **Verified** | SDK stage for build, runtime stage for execution |
| Secret exclusion | **Verified** | `.dockerignore` excludes `.env`, `*.pem`, `*.key`, `*.pfx`, `secrets/` |
| Trivy scanning | **Source-Complete** | CI/CD pipeline scans API and Agents images. Fails on CRITICAL/HIGH. |
| Dependency scanning | **Source-Complete** | `dotnet list package --vulnerable` in CI. |
| K8s security contexts | **Verified** | `runAsNonRoot: true`, `allowPrivilegeEscalation: false`, `readOnlyRootFilesystem: true`, `capabilities.drop: ["ALL"]` |
| K8s network policies | **Verified** | Ingress/egress restrictions per service |
| PostgreSQL credentials | **Verified** | K8s Secret with `secretKeyRef` (not plain env vars) |
| DB connection encryption | **Source-Complete** | `SslMode=Require` enforced via `PostgresConnectionStringBuilder.Harden()` |

---

## Health & Probes

| Service | Liveness | Readiness | Port |
|---|---|---|---|
| Gateway | HTTP `/healthz` | HTTP `/healthz` | 8080 |
| API | HTTP `/healthz/live` | HTTP `/healthz/ready` | 8080 |
| Runtime Worker | HTTP `/healthz/live` | HTTP `/healthz/ready` | 8081 |
| Scheduler Worker | HTTP `/healthz/live` | HTTP `/healthz/ready` | 8081 |
| Agents Worker | HTTP `/healthz/live` | HTTP `/healthz/ready` | 8081 |
| PostgreSQL | `pg_isready` | `pg_isready` | 5432 |

All worker health endpoints are implemented in `WorkerHealthService.cs` and registered in each worker's `Program.cs`. Kubernetes probe configuration in Helm templates matches these ports and paths.

---

## CI/CD Pipeline

| Stage | Status | Detail |
|---|---|---|
| Secret scanning | **Source-Complete** | Regex-based scan for hardcoded secrets |
| Build (zero warnings) | **Runtime-Proven** | `dotnet build -warnaserror` — 0 warnings |
| Unit tests | **Runtime-Proven** | 1,278 tests, 0 failures |
| Integration tests | **Runtime-Proven** | Testcontainers with Docker for PostgreSQL |
| Container scanning | **Source-Complete** | Trivy — SARIF output, fail on CRITICAL/HIGH |
| Dependency scanning | **Source-Complete** | `dotnet list package --vulnerable --include-transitive` |
| Image build | **Source-Complete** | Multi-platform (linux/amd64, linux/arm64), push to GHCR |
| Load testing | **Source-Complete** | k6 with 12 scenarios and `run-baselines.sh` automation. **No published baseline results.** |

---

## Database Deployment

| Aspect | Status | Detail |
|---|---|---|
| Migration framework | **Runtime-Proven** | DbUp with 26 numbered SQL scripts (001–026), journal table `schemaversions` |
| Migration health check | **Source-Complete** | `MigrationHealthCheck` reports pending migrations |
| Rollback scripts | **Source-Complete** | `Down/` directory with rollback for all 26 migrations (001–026) |
| Schema evolution | **Not Proven** | No real schema upgrade cycle has been executed in production |
| pgvector extension | **Source-Complete** | Script 001 creates extension. Required for memory/semantic search. |

---

## Scaling Posture

| Component | Horizontal Scaling | Constraint |
|---|---|---|
| Gateway | Safe (stateless proxy) | None |
| API | Safe (33 PostgreSQL-backed stores) | None |
| Runtime Workers | Safe (shared task queue) | Durable workflow file persistence is per-instance |
| Agents Workers | Safe (shared capability registry) | None |
| Scheduler | **Not scalable** | Must be single-replica. Coordinates globally. |
| PostgreSQL | Single-instance | HA requires external setup (RDS, Cloud SQL, patroni) |
| NATS | Single-instance | HA requires NATS cluster or managed service |

---

## Pre-Deployment Checklist

| # | Item | Required For |
|---|---|---|
| 1 | PostgreSQL 16+ with pgvector available | All modes |
| 2 | NATS JetStream available | All modes |
| 3 | `ArchonAIPersistence:ConnectionString` set | Production (else in-memory fallback) |
| 4 | JWT signing key ≥32 chars set | All modes |
| 5 | At least one model provider API key | AI execution |
| 6 | Connector credentials configured | Connector functionality |
| 7 | OIDC IdP configured per tenant | SSO login |
| 8 | TLS certificate on PostgreSQL server | DB connection encryption |
| 9 | Migrations 001–026 applied | First deployment |

---

## Deployment Truth Matrix

| Claim | Single-Instance | Multi-Instance (2+ API/Worker) | Evidence |
|---|---|---|---|
| State survives restart | **Yes** (with PostgreSQL) | **Yes** | 33 PostgreSQL-backed stores (31 via central persistence layer + 2 via domain-specific modules), 18 multi-instance tests |
| Agent selection is consistent | **Yes** | **Yes** | PostgreSQL-backed capability registry with SQL P95 |
| Pause/resume is cluster-wide | N/A | **Yes** | Single-row pattern, cross-instance tests |
| Workflow step persistence | **Yes** | **Per-instance only** | File-backed `DurableWorkflowExecutionEngine` |
| Scheduler coordination | **Yes** | **Single-replica only** | Not designed for multi-scheduler |
| Audit integrity | **Yes** | **Yes** | SHA-256 hash chain, append-only |

---

## Residual Deployment Gaps

| # | Gap | Impact | Mitigation Path |
|---|---|---|---|
| 1 | No live K8s deployment validated | Cannot confirm probe behavior, PDB eviction, network policy enforcement under real traffic | Deploy to staging cluster and run smoke tests |
| 2 | Vault providers not live-validated | Three vault `ISecretProvider` implementations exist (HashiCorp Vault, AWS SM, Azure KV) and are registered in the chain. Tested with mocks only. | Validate against live vault instances in staging |
| 3 | No load test baselines published | No evidence of behavior at scale | Execute k6 suite against staging, publish results |
| 4 | Grafana dashboards not validated | Dashboard JSON exists but not confirmed against real Prometheus scrape | Deploy monitoring stack and verify queries |
| 5 | Durable workflow is per-instance | Workflow step state in files, not shared across instances | Migrate to PostgreSQL-backed step persistence |
| 6 | PostgreSQL HA not addressed | Single PostgreSQL instance is SPOF | Document RDS/Cloud SQL/patroni recommendations |
| 7 | NATS HA not addressed | Single NATS instance is SPOF | Document NATS cluster or managed service options |

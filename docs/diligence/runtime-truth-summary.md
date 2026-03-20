# Runtime Truth Summary

Last verified: 2026-03-20

## Purpose

This document provides an honest, verifiable assessment of what ArchonAI can and cannot do at runtime. It distinguishes between source-complete (code exists), runtime-proven (tested under automated conditions), config-dependent (requires deployment-time configuration), and residual-risk items.

**Last Audited:** 2026-03-20
**Audit Method:** Source verification, test execution (979 unit tests, 0 failures), migration script count, DI registration count, Helm chart inspection.

---

## Evidence Tier Definitions

| Tier | Meaning | What It Proves |
|------|---------|----------------|
| **Runtime-Proven** | Automated tests pass, code exercised under test harness | Feature works under controlled conditions |
| **Source-Complete** | Code exists, compiles, wired into DI, but no test exercises the full path | Feature is structurally present but unproven at runtime |
| **Config-Dependent** | Requires deployment-time secrets, keys, or infrastructure | Feature cannot function without operator action |
| **Residual Risk** | Known gap with no current mitigation in source | Requires future work |

---

## 1. Persistence & Durability

| Capability | Evidence Tier | Detail |
|---|---|---|
| PostgreSQL-backed domain stores | **Runtime-Proven** | 22 stores via `ReplaceWithFactory` in `DependencyInjection.cs`. Includes RBAC, Audit, Governance, Trust Tiers, Decisions, Financial, Scenarios, Exceptions, Outcomes, Operational Twin, Enterprise Memory, Monitoring, Hero Workflows, Policy Simulation, Proof Analytics, Action Safety, Inspection, Agent Registry, Control Plane, Agent Capability Registry, Control Plane Alerts. |
| DbUp migration framework | **Runtime-Proven** | 25 numbered SQL scripts (001–025) with journal table, transaction-per-script, health check. Rollback scripts in `Down/`. |
| Multi-instance state consistency | **Runtime-Proven** | 18 Testcontainers integration tests verify cross-instance reads, upsert idempotency, cascade deletes, and pause-state sharing. |
| In-memory fallback (dev only) | **Source-Complete** | All 22 stores fall back to in-memory/file-backed when `ArchonAIPersistence:ConnectionString` is not set. Fallbacks are labeled non-production. |
| pgvector semantic search | **Source-Complete** | `PostgresMemoryRecordRepository` with vector embeddings. Requires PostgreSQL with pgvector extension. |
| IModelPerformanceTracker | **Source-Complete** | In-memory only. Rebuilds from live telemetry on startup. Acceptable — not persistent state. |

### Single-Instance vs Multi-Instance

| Aspect | Single-Instance | Multi-Instance |
|---|---|---|
| All 22 PostgreSQL stores | Safe | Safe (ON CONFLICT upserts, single-row patterns) |
| Durable workflow execution | Safe | **Caution** — File-backed step persistence is per-instance. Workflow can resume on same instance only. |
| Scheduler | Safe | **Caution** — Single-replica by design. Running >1 scheduler instance is not supported. |
| IClusterCoordinator | N/A | Transient per-instance — by design. Not shared state. |

---

## 2. AI Execution

| Capability | Evidence Tier | Detail |
|---|---|---|
| Intelligence loop (8 phases) | **Source-Complete** | Structurally complete with event bus instrumentation. |
| Model router (task-type routing) | **Source-Complete** | Routes by task type, cost, latency, quality preference. |
| OpenAI provider | **Config-Dependent** | Real HTTP client with retry. Requires `OPENAI_API_KEY`. |
| Anthropic provider | **Config-Dependent** | Real HTTP client with retry. Requires `ANTHROPIC_API_KEY`. |
| Azure OpenAI provider | **Config-Dependent** | Real HTTP client with retry. Requires `AZURE_OPENAI_API_KEY` + `AZURE_OPENAI_ENDPOINT`. |
| Local (Ollama) provider | **Config-Dependent** | Returns `IsSuccess: false` with `provider_unavailable` when Ollama is unreachable. Does not echo or fabricate output. |
| Echo guard in CompositeModelProvider | **Runtime-Proven** | `CompositeModelProvider` contains a guard that detects and logs `CRITICAL` if any external provider returns `FinishReason: "echo_fallback"`. This guard exists as a defensive check against third-party providers — it is not an output path used by ArchonAI's own providers. |

**Critical truth:** Without API keys, AI endpoints return structured errors (`IsSuccess: false`). `ModelProviderActivationService` logs `CRITICAL` at startup when no providers are active. `AiRuntimeDiagnostics` reports readiness tier `"unconfigured"`. No provider fabricates responses — the platform produces no AI output without keys. This is a deployment-time configuration requirement, not a code deficiency.

---

## 3. Identity & Authentication

| Capability | Evidence Tier | Detail |
|---|---|---|
| JWT HMAC-SHA256 auth | **Runtime-Proven** | 22 tests covering 11 attack vectors. |
| Refresh token rotation | **Runtime-Proven** | Tested in auth session E2E suite. |
| OIDC federation | **Runtime-Proven** | JWKS validation, nonce replay prevention, JIT provisioning. Tested with mock IdPs (3 test classes). |
| OIDC against live IdPs | **Residual Risk** | Not validated against Okta, Entra ID, or Auth0. |
| TOTP MFA | **Runtime-Proven** | Setup, verify, disable, recovery codes, org policy. 6 test classes. |
| WebAuthn/FIDO2 | **Runtime-Proven** | Register, authenticate, delete. Tested in unit tests. |
| TOTP secret encryption | **Runtime-Proven** | `DedicatedTotpSecretEncryptor`: AES-256-CBC + HMAC-SHA256, HKDF-derived keys from `ARCHONAI_TOTP_ENCRYPTION_KEY`. JWT fallback permitted only in dev/test (emits Warning). In production, `ProductionConfigValidator` raises Critical and `DedicatedTotpSecretEncryptor` throws if dedicated key is absent. |
| Multi-tenant isolation | **Runtime-Proven** | AsyncLocal scoping, cross-tenant prevention (9 tests), concurrent scope safety (50 parallel tasks). |

---

## 4. Authorization & Governance

| Capability | Evidence Tier | Detail |
|---|---|---|
| Three-tier RBAC | **Runtime-Proven** | Admin, Operator, Viewer. 23 tests. System roles immutable. |
| Deny-overrides-allow | **Runtime-Proven** | Deny policy wins over allow. |
| Governance gates | **Runtime-Proven** | Separation of duties, self-approval blocked, role-gated. 16 tests. |
| Approval workflows | **Runtime-Proven** | Approval policies, pending queue, history, deduplication. |
| Trust tiers | **Runtime-Proven** | Policy evaluation, tier-based action gating. |
| Trust lineage | **Source-Complete** | Composite endpoint aggregating decisions → approvals → actions → outcomes → proof. Typed frontend contracts. |

---

## 5. Connectors

| Capability | Evidence Tier | Detail |
|---|---|---|
| 6 specialized connectors (OAuth) | **Source-Complete** | Salesforce, HubSpot, QuickBooks, Slack, M365, Google Workspace. Real HTTP clients. |
| 4 generic connectors (HTTP) | **Source-Complete** | CRM, ERP, Financial, Messaging. Real HTTP clients with retry and audit. |
| Polly circuit breaker pipeline | **Runtime-Proven** | Timeout → Bulkhead → Circuit Breaker. Per-integration state tracking. Tested with mock handlers. |
| Connector health metrics | **Source-Complete** | Shadow counters for in-process health. Prometheus export uses OTel counters (shadow counters are health API only). |
| Live API validation | **Residual Risk** | All connectors tested with mock HTTP handlers only. No live sandbox validation. |

---

## 6. Observability

| Capability | Evidence Tier | Detail |
|---|---|---|
| OpenTelemetry tracing + metrics | **Source-Complete** | Registered in API and Gateway. |
| Serilog structured logging | **Source-Complete** | Console + file sinks. |
| Prometheus `/metrics` | **Source-Complete** | Endpoint configured. Not validated against live Prometheus. |
| 5 API health checks | **Source-Complete** | EventBus, TaskQueue, Connectors, ModelProviders, StartupReadiness. |
| Worker health endpoints | **Runtime-Proven** | Runtime, Scheduler, Agents: `/healthz/live` + `/healthz/ready` on port 8081. |
| 10 Grafana dashboards | **Source-Complete** | JSON dashboard files. Not validated against live scrape data. |
| Prometheus alert rules | **Source-Complete** | Alert rules and AlertManager config. Not validated in live alerting stack. |
| Log aggregation (EFK/Loki) | **Residual Risk** | No log aggregation pipeline configured. |

---

## 7. Compliance & Audit

| Capability | Evidence Tier | Detail |
|---|---|---|
| SHA-256 hash-chained audit log | **Runtime-Proven** | Append-only, integrity verification. |
| Data retention automation | **Source-Complete** | `RetentionHostedService` runs daily. Configurable per data type. Not validated through a full retention cycle. |
| GDPR data export (Art. 15/20) | **Source-Complete** | Identity, audit, memory, decisions export. |
| GDPR right to erasure (Art. 17) | **Source-Complete** | Soft-delete, anonymization, erasure certificate with SHA-256 hash. |
| Legal/DPO sign-off | **Residual Risk** | Implementation exists but no compliance review. |

---

## 8. Secret Management

| Capability | Evidence Tier | Detail |
|---|---|---|
| `ISecretProvider` abstraction | **Runtime-Proven** | Interface with `ChainedSecretProvider`. Full chain: HashiCorp Vault → AWS Secrets Manager → Azure Key Vault → File → Environment. |
| `RotatingJwtSecurityKeyProvider` | **Source-Complete** | JWT key rotation support with dual-key validation during rollover. |
| HashiCorp Vault provider | **Implemented in Source** | `HashiCorpVaultSecretProvider`: KV v2 via HTTP API, AppRole auth, token renewal at 75% TTL, version-based rotation polling. Tested with mock HTTP handlers. |
| AWS Secrets Manager provider | **Implemented in Source** | `AwsSecretsManagerSecretProvider`: SDK credential chain (IAM/instance profile/env), JSON secret parsing, rotation detection via DescribeSecret. Tested with SDK-absent degradation. |
| Azure Key Vault provider | **Implemented in Source** | `AzureKeyVaultSecretProvider`: `DefaultAzureCredential` (Managed Identity), version tracking, rotation polling. Tested with SDK-absent degradation. |
| TOTP secret encryption | **Runtime-Proven** | `DedicatedTotpSecretEncryptor` with `ARCHONAI_TOTP_ENCRYPTION_KEY` (HKDF-derived AES-256-CBC + HMAC-SHA256). JWT fallback dev/test only. Health-gated via `ProductionConfigValidator`. |

---

## Test Evidence Summary

| Suite | Count | Status |
|---|---|---|
| Unit tests (10 assemblies) | 979 | All pass |
| Enterprise tests | 463 | All pass |
| Integration tests (Testcontainers) | 18+ multi-instance | All pass |
| Frontend contract tests | 11 | All pass (vitest) |
| Load tests (k6) | 4 scenarios | Infrastructure exists, **no published baselines** |

---

## What This Platform Can Honestly Do Today

1. **Single-instance deployment with PostgreSQL**: All 22 stores are durable. Survives restarts. Full RBAC, governance, audit, trust, decisions, memory, observability.
2. **Multi-instance deployment**: 22 PostgreSQL stores are concurrency-safe. Agent Registry, Control Plane, and capability profiles are shared across instances. Scheduler must remain single-replica.
3. **Enterprise identity**: JWT auth, TOTP MFA, WebAuthn, OIDC federation (with mock IdPs), multi-tenant isolation.
4. **Governance and trust**: Approval gates, trust tiers, trust lineage tracing, action safety classification, proof analytics.
5. **Connector framework**: 10 connectors with real HTTP clients, Polly circuit breakers, rate limiting, audit events — all tested with mocks, none against live APIs.

## What This Platform Cannot Do Today Without Operator Action

1. **Produce real AI output** — Requires API keys for at least one model provider.
2. **Connect to real CRM/ERP/Financial APIs** — Requires credentials and endpoint configuration.
3. **Federate with a live IdP** — OIDC is implemented but not tested against Okta/Entra/Auth0.
4. **Protect secrets with an external vault at runtime** — Three vault `ISecretProvider` implementations exist (HashiCorp Vault, AWS Secrets Manager, Azure Key Vault) and are registered in the DI chain. Production deployment requires vault connectivity and credentials.
5. **Aggregate logs centrally** — No EFK/Loki pipeline.

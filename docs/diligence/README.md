# ArchonAI — Technical Diligence Pack

Last verified: 2026-03-20

## Purpose

This pack provides everything a buyer, investor, enterprise pilot customer, or CTO needs to evaluate ArchonAI quickly and accurately. Every claim is linked to code, tests, or configuration — nothing requires trust.

## How to Use This Pack

| Audience | Start Here | Time |
|---|---|---|
| CTO / Technical buyer | [Technical Summary](technical-summary.md) | 15 min |
| Security / Compliance | [Security Summary](security-summary.md) | 10 min |
| Solutions Architect | [Demo Guide](demo-guide.md) → run the demo | 30 min |
| Investor / Board | This README → Technical Summary | 10 min |

---

## Platform Overview

ArchonAI is an enterprise autonomous operations platform that orchestrates AI agents across business functions — Sales, Finance, Operations, Marketing, and Support. Agents observe business signals, generate goals, evaluate strategies, simulate outcomes, execute tasks, and learn from results.

### Architecture at a Glance

```
                    ┌──────────────┐
                    │   Gateway    │  YARP reverse proxy, rate limiting, JWT auth
                    │   (port 80)  │
                    └──────┬───────┘
                           │
                    ┌──────┴───────┐
                    │   REST API   │  59 projects, 17 route groups, v1/v2 versioning
                    │  (port 8080) │
                    └──────┬───────┘
                           │
          ┌────────────────┼────────────────┐
          │                │                │
   ┌──────┴──────┐  ┌─────┴──────┐  ┌──────┴──────┐
   │   Runtime   │  │  Scheduler │  │   Agents    │
   │   Worker    │  │   Worker   │  │   Worker    │
   │  (2 pods)   │  │  (1 pod)   │  │  (3 pods)   │
   └──────┬──────┘  └─────┬──────┘  └──────┬──────┘
          │                │                │
          └────────────────┼────────────────┘
                           │
              ┌────────────┼────────────┐
              │                         │
       ┌──────┴──────┐          ┌──────┴──────┐
       │ PostgreSQL  │          │    NATS     │
       │  + pgvector │          │  JetStream  │
       └─────────────┘          └─────────────┘
```

### Key Numbers

| Metric | Value |
|---|---|
| Source projects | 59 |
| Deployable services | 7 (Gateway, API, Runtime, Scheduler, Agents, PostgreSQL, NATS) |
| Enterprise connectors | 10 (6 with real OAuth/HTTP, 4 generic stubs) |
| Specialized agent modules | 5 (Operations, Finance, Sales, Marketing, Support) |
| Intelligence loop phases | 8 (Perception → Goal Generation → Strategy → Simulation → Task Planning → Execution → Evaluation → Learning) |
| API route groups | 17 |
| Enterprise verification tests | 168 |
| Proven enterprise claims | 57 |
| Configuration sections | 40+ |

---

## What Is Fully Implemented

These components have real implementations, test coverage, and configuration:

- **Deterministic workflow engine** — State machine with 8 states, invalid transition rejection, concurrent safety, file-backed durable execution with step-level retry
- **RBAC** — 3 system roles (Admin, Operator, Viewer), 16 permissions, deny-overrides-allow, immutable system roles, audit events on all mutations
- **Multi-tenant isolation** — AsyncLocal scope management, per-tenant resource quotas, cross-tenant access prevention
- **Governance & approval gates** — Separation of duties, self-approval blocking, role-gated approvals, tenant-scoped deduplication
- **Policy engine** — Multi-factor risk scoring, forbidden capability blocking, confidence thresholds, manual overrides
- **Audit trail** — SHA-256 hash-chained entries, integrity verification, category-based querying
- **JWT authentication** — HMAC-SHA256 signing, refresh token rotation, invite flows, attack vector coverage (forgery, expiry, algorithm confusion, tampering)
- **Connector framework** — 6 connectors with real OAuth/HTTP (Salesforce, HubSpot, QuickBooks, Slack, Microsoft 365, Google Workspace), exponential backoff retry, rate limit tracking, semaphore concurrency control
- **Intelligence loop** — 8-phase autonomous cycle with full event bus instrumentation
- **Deployment** — Docker Compose for local dev, Kubernetes manifests + Helm chart for production

## What Is Partially Implemented or Stubbed

- **LLM model providers** — 4 providers (OpenAI, Anthropic, Azure OpenAI, Local/Ollama) have real HTTP client code. All return hard errors (`IsSuccess: false`) when API keys are absent — no fabricated output. `ModelProviderActivationService` logs `CRITICAL` at startup when no providers are active.
- **Connector live validation** — All 10 connectors (6 specialized + 4 generic) use real HTTP clients with OAuth, retry, and circuit breakers. Tested with mock handlers only — no live sandbox validation.

## Implemented Since Prior Audit (No Longer Gaps)

The following were previously listed as "Not Implemented" and are now source-complete or runtime-proven:

- **SSO/OIDC** — Full federation with JWKS verification, nonce validation, JIT provisioning, per-tenant IdP config. Tested with mock IdPs.
- **MFA** — TOTP + WebAuthn (FIDO2) with enrollment, verification, recovery codes, org-level policy. 6 test classes.
- **Secret vault integration** — Three vault-backed `ISecretProvider` implementations: HashiCorp Vault (AppRole), AWS Secrets Manager, Azure Key Vault. Full chain with graceful degradation.
- **Database persistence** — 33 PostgreSQL-backed stores (31 via central persistence layer + 2 via domain-specific modules). 26 numbered migration scripts with complete rollback coverage.
- **Container security scanning** — Trivy in CI/CD pipeline.
- **Dependency vulnerability scanning** — `dotnet list package --vulnerable` in CI.
- **TOTP secret encryption** — `DedicatedTotpSecretEncryptor` with dedicated key, HKDF-derived AES-256-CBC + HMAC-SHA256, health-gated production enforcement.

---

## Diligence Documents

| Document | Contents |
|---|---|
| [Technical Summary](technical-summary.md) | Implementation status matrix, architecture decisions, technology stack, what's real vs roadmap |
| [Security Summary](security-summary.md) | Security controls, attack coverage, unverified areas, compliance readiness |
| [Demo Guide](demo-guide.md) | How to run the platform locally, scripted demo paths, what to expect |

## Enterprise Test Evidence

| Document | Contents |
|---|---|
| [Enterprise Proof Pack](../enterprise/enterprise-proof-pack.md) | 57 proven claims mapped to 168 automated tests |
| [Security Verification](../enterprise/security-verification.md) | Security controls mapped to specific test evidence |
| [Test Strategy](../enterprise/test-strategy.md) | Test taxonomy, principles, coverage matrix |

## How to Verify Any Claim

```bash
# Run all 168 enterprise verification tests (< 3 seconds, zero external dependencies)
cd archonai
dotnet test tests/ArchonAI.Enterprise.Tests/ --verbosity normal

# Run security-specific tests
dotnet test tests/ArchonAI.Enterprise.Tests/ --filter "Namespace~Security"

# Run end-to-end flows
dotnet test tests/ArchonAI.Enterprise.Tests/ --filter "Namespace~EndToEnd"

# Start the full platform locally
docker compose -f archonai/docker-compose.yml up --build
```

---

## Strongest Diligence Impressions

1. **Test-backed enterprise claims** — 168 automated tests prove 57 specific claims. No hand-waving.
2. **Security depth** — Auth bypass, injection, cross-tenant, permission boundary, and configuration security all have dedicated test suites with real attack patterns.
3. **Architecture maturity** — 59-project modular solution with clear separation of concerns, versioned APIs, and production deployment manifests.
4. **Connector quality** — 6 real-world connectors with OAuth, retry, rate limiting, and concurrency control.
5. **Governance rigor** — Separation of duties, approval gates, and risk scoring are fully implemented and tested.

## Weakest Diligence Impressions

1. **No live AI output** — All 4 model providers return hard errors (`IsSuccess: false`) without API keys. The platform produces no AI output without configuration — this is a deployment-time requirement.
2. **No live IdP validation** — OIDC federation is implemented and tested with mock IdPs but not validated against Okta, Entra ID, or Auth0.
3. **No published load test baselines** — k6 infrastructure with 12 scenarios exists, but no baseline results have been captured or published.
4. **Vault providers not live-validated** — Three vault `ISecretProvider` implementations exist but have been tested only with mock handlers, not against live vault instances.
5. **Durable workflow persistence is per-instance** — File-backed step state, not shared across instances in multi-replica deployments.

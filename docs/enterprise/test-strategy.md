# Enterprise Test Strategy

## Overview

This document defines ArchonAI's enterprise verification test strategy — the framework by which marketing claims, architectural promises, and security guarantees are validated through automated tests.

## Test Taxonomy

### 1. Integration Tests (`ArchonAI.Enterprise.Tests/Integration/`)

Tests that exercise real service implementations with in-memory dependencies, verifying that components integrate correctly.

| Test Suite | Focus Area | Test Count |
|---|---|---|
| `PolicyEngineIntegrationTests` | Risk scoring, forbidden capabilities, approval gating, manual overrides, confidence thresholds | 12 |
| `WorkflowStateMachineTests` | State transitions, invalid transition rejection, concurrent access safety, lifecycle completeness | 13 |
| `RbacIntegrationTests` | Role hierarchy, permission evaluation, system role immutability, policy-based access control | 12 |
| `AuditLogIntegrationTests` | Entry recording, hash-chain integrity, query filtering, pagination, category statistics | 10 |
| `MultiTenantContextTests` | Scope lifecycle, nested scopes, async flow preservation, concurrent isolation | 7 |
| `TenantResourceGovernorTests` | Planning slot limits, task count limits, cross-tenant resource isolation | 6 |
| `ConnectorResilienceTests` | Transient failure retry, rate limit handling, authentication failure, audit event emission | 8 |

### 2. Security Tests (`ArchonAI.Enterprise.Tests/Security/`)

Tests that validate security controls against real attack patterns.

| Test Suite | Focus Area | Test Count |
|---|---|---|
| `AuthBypassTests` | JWT forgery, expired tokens, wrong issuer/audience, none algorithm, payload tampering | 11 |
| `CrossTenantAccessTests` | Tenant isolation in auth, approvals, history, context, and resource governors | 9 |
| `PermissionBoundaryTests` | Privilege escalation prevention, system role protection, separation of duties, RBAC auditing | 11 |
| `InputValidationTests` | SQL injection, XSS payloads, boundary values, unicode, null bytes, password security | 16 |
| `InsecureConfigurationTests` | Default policy thresholds, tenant limits, governance defaults, approval requirements | 11 |

### 3. API Contract Tests (`ArchonAI.Enterprise.Tests/Contract/`)

Tests that verify service response shapes remain stable for API consumers.

| Test Suite | Focus Area | Test Count |
|---|---|---|
| `ApiContractTests` | RBAC role/status shapes, approval gate shapes, audit entry shapes, query result shapes | 8 |

### 4. End-to-End Tests (`ArchonAI.Enterprise.Tests/EndToEnd/`)

Tests that exercise cross-cutting enterprise flows involving multiple services.

| Test Suite | Focus Area | Test Count |
|---|---|---|
| `WorkflowGovernanceE2ETests` | Workflow + approval + RBAC + audit integration, tenant isolation, retry + audit trail | 5 |
| `AuthSessionE2ETests` | Full session lifecycle, invite flow, duplicate prevention, token rotation | 6 |

## Test Principles

### Real Business Risk Focus
Every test maps to a concrete enterprise risk scenario — not synthetic code coverage. Tests validate behaviors that customers and auditors would care about.

### No Token Tests
Tests that merely assert trivially true conditions (e.g., `Assert.True(true)`) are excluded. Every assertion validates meaningful system behavior.

### Maintainability
- Tests use the same dependency injection patterns as production code
- Mock handlers are reusable across connector tests
- Test helpers are minimal and focused

### Determinism
- All tests use in-memory stores for isolation
- Async tests use `ConcurrentBag` for thread-safe error collection
- Temporary files are cleaned up in `Dispose()`

## Coverage Matrix

| Enterprise Claim | Test Type | Status |
|---|---|---|
| Multi-tenant isolation | Integration + Security | **Proven** |
| RBAC enforcement | Integration + Security | **Proven** |
| Immutable audit logs | Integration + Contract | **Proven** |
| Governance approval gates | Integration + E2E | **Proven** |
| Workflow state machine | Integration | **Proven** |
| Policy-based risk scoring | Integration | **Proven** |
| Connector retry/resilience | Integration | **Proven** |
| JWT authentication | Security | **Proven** |
| Cross-tenant attack prevention | Security | **Proven** |
| Input validation / injection defense | Security | **Proven** |
| Secure default configuration | Security | **Proven** |
| Session lifecycle management | E2E | **Proven** |
| AI execution (real LLM calls) | — | **Unproven** |
| Database persistence durability | — | **Partially proven** (file-backed) |
| Load/performance under scale | — | **Unproven** |
| SSO/OIDC integration | — | **Unproven** (not implemented) |
| Secret management (vault) | — | **Unproven** (not implemented) |

## Running the Tests

```bash
# Run all enterprise verification tests
dotnet test tests/ArchonAI.Enterprise.Tests/

# Run by category
dotnet test tests/ArchonAI.Enterprise.Tests/ --filter "Namespace~Integration"
dotnet test tests/ArchonAI.Enterprise.Tests/ --filter "Namespace~Security"
dotnet test tests/ArchonAI.Enterprise.Tests/ --filter "Namespace~EndToEnd"
dotnet test tests/ArchonAI.Enterprise.Tests/ --filter "Namespace~Contract"
```

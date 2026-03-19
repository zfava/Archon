# RBAC & TrustTier Persistence Integration Tests

## Overview

These tests prove that RBAC state (roles, assignments, policies, access decisions) and TrustTier state (tier policies, evaluations, tenant seeding) survive service re-instantiation when backed by PostgreSQL. They exercise the real `PostgresRbacStore` and `PostgresTrustTierStore` against a real database — not mocks, not the in-memory fallback.

## Strategy

**Testcontainers-based ephemeral PostgreSQL.** Each test run starts a real `postgres:16-alpine` Docker container, applies the RBAC and TrustTier DDL, runs the tests, and destroys the container. No shared state between runs. No external database required.

### Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| **Testcontainers (not shared DB)** | Hermetic, CI-safe, no infrastructure dependency |
| **Direct DDL (not full DbUp migration runner)** | Avoids `pgvector` extension dependency from migration 001; RBAC and TrustTier tables are self-contained |
| **New store instance per verification step** | Each `CreateRbacStore()` / `CreateTrustTierStore()` simulates a process restart |
| **Table truncation between tests** | Test isolation without container restart overhead |
| **NoOpEventBus** | PostgresRbacStore requires IEventBus; the no-op stub satisfies the constructor without side effects |
| **Shared fixture for both stores** | Single container hosts both RBAC and TrustTier tables, reducing startup overhead |

## RBAC Test Matrix

| # | Test | What It Proves |
|---|------|----------------|
| 1 | `CreateRole_PersistsAndRetrievesCorrectly` | Basic role CRUD round-trip with JSONB permissions |
| 2 | `Role_SurvivesStoreReinstantiation` | **Restart proof** — role created by instance 1 is readable by instance 2 |
| 3 | `RoleUpdate_PersistsAcrossInstances` | Description and permission updates survive restart |
| 4 | `RoleDelete_PersistsAcrossInstances` | Deletion persists across restart |
| 5 | `SystemRole_CannotBeModifiedOrDeleted` | System role immutability enforced at store level |
| 6 | `AssignmentLifecycle_AssignAndRevoke_PersistsAcrossInstances` | Full assignment lifecycle (assign → verify → revoke) across restarts |
| 7 | `AssignmentCascade_DeletingRoleClearsAssignments` | FK CASCADE deletes assignments when role is deleted |
| 8 | `PolicyCrud_PersistsAcrossInstances` | Policy create, disable, delete lifecycle across restarts |
| 9 | `AccessEvaluation_PolicyDenyTakesPrecedence_AcrossInstances` | Deny policy overrides allow policy in access evaluation after restart |
| 10 | `EffectivePermissions_AggregateAcrossRoles_SurvivesRestart` | Multi-role permission aggregation (union) persists across restart |
| 11 | `GetRoles_ListsAllRoles_AcrossInstances` | Role listing returns all roles after restart |
| 12 | `GetStatus_ReflectsAccurateCounts` | Status endpoint reflects real database counts |

## TrustTier Test Matrix

| # | Test | What It Proves |
|---|------|----------------|
| 1 | `ListPolicies_AutoSeedsDefaultsForNewTenant` | 5 default policies auto-seeded per new tenant |
| 2 | `AutoSeeding_SurvivesStoreReinstantiation` | Seeded policies survive restart; no duplicate seeding |
| 3 | `SetPolicy_PersistsAcrossInstances` | Custom policy via upsert survives restart with all fields |
| 4 | `SetPolicy_UpsertUpdatesExistingPolicy` | ON CONFLICT upsert updates existing policy correctly |
| 5 | `Evaluate_ConfidenceGate_DemotesEffectiveTier` | Low confidence demotes tier to DraftApprovalRequired |
| 6 | `Evaluate_ValueCeilingGate_DemotesEffectiveTier` | Value exceeding ceiling demotes tier |
| 7 | `Evaluate_ReversibilityGate_DemotesEffectiveTier` | Irreversible action demotes tier when policy requires reversibility |
| 8 | `Evaluate_PolicyPersistsAndEvaluatesCorrectlyAfterRestart` | Evaluation using persisted policy after restart |
| 9 | `DeletePolicy_TenantIsolation_CannotDeleteCrossTenant` | Cross-tenant delete blocked at SQL level |
| 10 | `ListPolicies_TenantIsolation_OnlySeesOwnPolicies` | Each tenant sees only their own policies |
| 11 | `GetEffectiveTier_ReflectsSetPolicy_AcrossInstances` | Effective tier lookup reflects custom policy after restart |
| 12 | `GetTierMap_ReflectsAllPolicies_AcrossInstances` | Full tier map matches seeded defaults after restart |
| 13 | `DeletePolicy_PersistsAcrossInstances` | Policy deletion persists across restart |

## Running

### Prerequisites

- Docker must be running (Testcontainers manages the container lifecycle)
- No external PostgreSQL instance needed

### Run only RBAC/TrustTier persistence integration tests

```bash
dotnet test archonai/tests/ArchonAI.Enterprise.Tests \
  --filter "Category=Integration&Database=PostgreSQL"
```

### Run all tests excluding slow integration tests

```bash
dotnet test archonai/tests/ArchonAI.Enterprise.Tests \
  --filter "Category!=Integration"
```

## Architecture

```
PostgresRbacTrustTierFixture (IAsyncLifetime)
├── Starts postgres:16-alpine via Testcontainers
├── Creates archonai schema + RBAC tables (003) + TrustTier tables (005)
├── Provides CreateRbacStore() → new PostgresRbacStore per call
├── Provides CreateTrustTierStore() → new PostgresTrustTierStore per call
├── NoOpEventBus singleton for RBAC store's IEventBus dependency
└── CleanTablesAsync() → TRUNCATE between tests

PostgresRbacPersistenceTests
├── [Collection("PostgresRbacTrustTier")] — shares fixture
├── IAsyncLifetime.InitializeAsync → CleanTablesAsync()
└── 12 tests covering roles, assignments, policies, access evaluation

PostgresTrustTierPersistenceTests
├── [Collection("PostgresRbacTrustTier")] — shares fixture
├── IAsyncLifetime.InitializeAsync → CleanTablesAsync()
└── 13 tests covering policies, evaluation, guardrails, tenant isolation
```

## Residual Gaps

1. **Concurrent write contention**: Tests are serial within the collection. Concurrent upserts to the same policy are not tested.
2. **Large dataset performance**: Tests use small datasets. Index effectiveness under load is not validated.
3. **Migration runner integration**: Tests apply DDL directly, not via the DbUp `MigrationRunner`. A separate migration integration test would verify the runner itself.
4. **RBAC SeedSystemRolesAsync**: The store's `EnsureInitializedAsync` does not call `SeedSystemRolesAsync` — system role seeding is not triggered by the store itself. Tests manually insert system roles where needed.

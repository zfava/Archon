# Governance Persistence Integration Tests

## Overview

These tests prove that governance state (approval gates, policies, audit entries) survives service re-instantiation when backed by PostgreSQL. They exercise the real `PostgresGovernanceStore` against a real database — not mocks, not the in-memory fallback.

## Strategy

**Testcontainers-based ephemeral PostgreSQL.** Each test run starts a real `postgres:16-alpine` Docker container, applies the governance DDL, runs the tests, and destroys the container. No shared state between runs. No external database required.

### Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| **Testcontainers (not shared DB)** | Hermetic, CI-safe, no infrastructure dependency |
| **Direct DDL (not full DbUp migration runner)** | Avoids `pgvector` extension dependency from migration 001; governance tables are self-contained |
| **New store instance per verification step** | Each `_fixture.CreateStore()` simulates a process restart — the critical persistence proof |
| **Table truncation between tests** | Test isolation without container restart overhead |
| **xUnit `ICollectionFixture`** | Container started once, shared across all tests in the collection |

## Test Matrix

| # | Test | What It Proves |
|---|------|----------------|
| 1 | `CreateApprovalGate_PersistsAndRetrievesCorrectly` | Basic CRUD round-trip against real PostgreSQL |
| 2 | `ApprovalGate_SurvivesStoreReinstantiation` | **Restart proof** — data created by instance 1 is readable by instance 2 |
| 3 | `StatusTransition_Approve_PersistsAcrossInstances` | Approval status change survives restart |
| 4 | `StatusTransition_Deny_PersistsCorrectly` | Denial status persists |
| 5 | `FullLifecycle_CreateReviewExecute_AllPersistedAcrossInstances` | Complete lifecycle (request → approve → execute) survives restart with audit trail |
| 6 | `ExecutionFailure_ErrorPersistsAcrossInstances` | Execution error string survives restart |
| 7 | `TenantIsolation_CrossTenantAccess_Blocked` | Cross-tenant reads and reviews are blocked at the database query level |
| 8 | `TenantIsolation_PersistsAcrossInstances` | Tenant isolation holds after restart |
| 9 | `Deduplication_SameActionResourceTenant_ReturnsSameGate` | Pending-gate deduplication works at the SQL level |
| 10 | `ApprovalPolicy_PersistsAcrossInstances` | Custom policies survive restart and `RequiresApprovalAsync` resolves correctly |
| 11 | `AuditHistory_PersistsAcrossInstances` | Audit entries survive restart with all fields intact |

## Running

### Prerequisites

- Docker must be running (Testcontainers manages the container lifecycle)
- No external PostgreSQL instance needed

### Run only persistence integration tests

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
PostgresGovernanceFixture (IAsyncLifetime)
├── Starts postgres:16-alpine via Testcontainers
├── Creates archonai schema + governance tables (DDL)
├── Provides CreateStore() → new PostgresGovernanceStore per call
└── CleanTablesAsync() → TRUNCATE between tests

PostgresGovernancePersistenceTests
├── [Collection("PostgresGovernance")] — shares fixture
├── IAsyncLifetime.InitializeAsync → CleanTablesAsync()
└── Each test creates 1-2 store instances to prove cross-instance persistence
```

## Residual Gaps

1. **Concurrent write contention**: Tests are serial within the collection. Concurrent writes to the same gate are not tested.
2. **Large dataset performance**: Tests use small datasets. Index effectiveness under load is not validated.
3. **Migration runner integration**: Tests apply DDL directly, not via the DbUp `MigrationRunner`. A separate migration integration test would verify the runner itself.

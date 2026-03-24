# Testcontainers CI Execution Proof

## Overview

This document explains how the CI pipeline proves that PostgreSQL/Testcontainers-backed integration tests are **actually executed** — not merely present in the codebase, and not silently skipped.

## Prior Ambiguity

Before this change, the CI workflow ran `dotnet test` on the entire solution with no filters and no verbosity. This meant:

1. **No separation** between unit tests and container-backed integration tests in CI output.
2. **No explicit Docker verification** — if Docker were unavailable, Testcontainers tests could fail silently or be skipped without the workflow failing.
3. **TRX results uploaded but never inspected** — a reviewer downloading the artifact could see results, but the CI itself never asserted that integration tests were discovered and passed.
4. **No verbose logging** — container startup, schema creation, and teardown events were invisible in CI logs.

## How Execution Is Now Proved

### 1. Docker Availability Gate

A dedicated step verifies Docker is available before any tests run:

```yaml
- name: Verify Docker available (required for Testcontainers)
  run: |
    docker version
    docker info --format '{{.ServerVersion}}'
```

If Docker is not available, this step fails and the entire test job stops. Testcontainers requires Docker — this makes the dependency explicit.

### 2. Separate Integration Test Step

Integration tests (tagged `Category=Integration` or `Category=RuntimeProof`) run in their own step with verbose console output:

```yaml
- name: Run Testcontainers integration tests
  run: >
    dotnet test ${{ env.SOLUTION_PATH }}
    --filter "Category=Integration|Category=RuntimeProof"
    --logger "console;verbosity=detailed"
    --logger "trx;LogFileName=integration-results.trx"
```

The verbose console logger surfaces:
- Each test name as it starts and finishes
- Testcontainers container startup and connection logs
- Schema creation SQL execution
- Any failures with full stack traces

### 3. Post-Execution Verification

A mandatory verification step parses the TRX results file and enforces:

| Check | Failure Condition |
|-------|-------------------|
| TRX file exists | No integration-results.trx found |
| Tests discovered | `total=0` (tests silently skipped) |
| Tests passed | `passed=0` (all failed or skipped) |
| No failures | `failed > 0` |

This step runs with `if: always()` so it executes even if the test step fails — providing a clear diagnostic.

## Test Inventory

The following Testcontainers-backed test classes are covered:

| Test Class | Fixture | Trait | Container |
|------------|---------|-------|-----------|
| `PostgresGovernancePersistenceTests` | `PostgresGovernanceFixture` | Integration | postgres:16-alpine |
| `PostgresRbacPersistenceTests` | `PostgresRbacTrustTierFixture` | Integration | postgres:16-alpine |
| `PostgresTrustTierPersistenceTests` | `PostgresRbacTrustTierFixture` | Integration | postgres:16-alpine |
| `PostgresAgentRegistryPersistenceTests` | `PostgresAgentRegistryControlPlaneFixture` | Integration | postgres:16-alpine |
| `PostgresControlPlanePersistenceTests` | `PostgresAgentRegistryControlPlaneFixture` | Integration | postgres:16-alpine |
| `PostgresAgentCapabilityRegistryPersistenceTests` | `PostgresAgentRegistryControlPlaneFixture` | Integration | postgres:16-alpine |
| `PostgresControlPlaneAlertPersistenceTests` | `PostgresAgentRegistryControlPlaneFixture` | Integration | postgres:16-alpine |
| `GovernanceLifecycleRuntimeTests` | `PostgresGovernanceFixture` | RuntimeProof | postgres:16-alpine |
| `RetentionServiceRuntimeTests` | Own container | RuntimeProof | postgres:16-alpine |

## What Each Test Proves

- **Persistence across store re-instantiation**: Data written by Store Instance 1 is readable by Store Instance 2 (simulates service restart).
- **Tenant isolation**: Cross-tenant data access is blocked at the SQL level, not just the application level.
- **Governance lifecycle**: Full request-approve-execute flow persists all state transitions.
- **Separation of duties**: Requester cannot approve their own request (enforced in the real store, not mocked).
- **Retention sweep**: Production SQL `DELETE` statements execute against real PostgreSQL, with row counts verified.

## CI Artifact

After every CI run, the `test-results` artifact contains:
- `unit-results.trx` — unit test results
- `integration-results.trx` — Testcontainers integration test results (the proof artifact)

A reviewer can download `integration-results.trx` and inspect individual test outcomes.

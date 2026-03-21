# Runtime Proof Pack

This document catalogs ArchonAI's runtime-proof artifacts — tests and verification scripts that demonstrate operational behavior beyond static code presence.

## Proof Targets

| # | Claim | Source File(s) | Runtime Proof | Status |
|---|-------|---------------|---------------|--------|
| 1 | Worker health endpoints respond on /healthz/live and /healthz/ready | `Infrastructure/Health/WorkerHealthService.cs` | `WorkerHealthRuntimeTests` | Covered |
| 2 | Readiness probe returns 503 when dependencies are down | `Infrastructure/Health/WorkerHealthService.cs`, `WorkerHealthExtensions.cs` | `WorkerHealthRuntimeTests.ReadinessProbe_ReportsNotReady_WhenAnyDependencyFails` | Covered |
| 3 | Readiness probe times out within 50ms budget | `WorkerHealthService.cs:81` (40ms CancelAfter) | `WorkerHealthRuntimeTests.ReadinessProbe_TimesOut_Returns503Equivalent` | Covered |
| 4 | Startup readiness blocks traffic until all components initialized | `Common/Observability/HealthChecks.cs` (StartupReadinessCheck) | `WorkerHealthRuntimeTests.StartupReadiness_*` | Covered |
| 5 | Task queue health degrades/fails at thresholds (500/2000) | `Common/Observability/HealthChecks.cs` (TaskQueueHealthCheck) | `WorkerHealthRuntimeTests.TaskQueueHealth_*` | Covered |
| 6 | Connector health aggregates and reports degraded connectors | `Common/Observability/HealthChecks.cs` (ConnectorHealthCheck) | `WorkerHealthRuntimeTests.ConnectorHealth_*` | Covered |
| 7 | Model provider health degrades after consecutive failures | `Common/Observability/HealthChecks.cs` (ModelProviderHealthCheck) | `WorkerHealthRuntimeTests.ModelProviderHealth_*` | Covered |
| 8 | Retention sweep deletes expired rows from audit_log | `Infrastructure/RetentionHostedService.cs` | `RetentionServiceRuntimeTests.Sweep_DeletesExpiredAuditRows_PreservesRecent` | Covered |
| 9 | Retention sweep deletes expired telemetry rows | `Infrastructure/RetentionHostedService.cs` | `RetentionServiceRuntimeTests.Sweep_DeletesExpiredTelemetryRows_PreservesRecent` | Covered |
| 10 | Retention sweep records to retention_log table | `Infrastructure/RetentionHostedService.cs:102-129` | `RetentionServiceRuntimeTests.Sweep_RecordsSweepResult_InRetentionLog` | Covered |
| 11 | Retention sweep gracefully handles empty tables | `Infrastructure/RetentionHostedService.cs` | `RetentionServiceRuntimeTests.Sweep_EmptyTables_CompletesWithZeroDeletes` | Covered |
| 12 | Retention sweep no-ops when connection string is absent | `Infrastructure/RetentionHostedService.cs:48-51` | `RetentionServiceRuntimeTests.Sweep_NoConnectionString_ReturnsZeroResult` | Covered |
| 13 | Governance approval gates survive store restart (Postgres) | `Persistence/Stores/PostgresGovernanceStore.cs` | `GovernanceLifecycleRuntimeTests.FullLifecycle_RequestApproveExecute_SurvivesRestart` | Covered |
| 14 | Separation of duties enforced at approval time | `PostgresGovernanceStore.cs:237-242` | `GovernanceLifecycleRuntimeTests.SeparationOfDuties_RequesterCannotApproveOwnRequest` | Covered |
| 15 | Role-based approval constraints enforced | `PostgresGovernanceStore.cs:229-234` | `GovernanceLifecycleRuntimeTests.RoleEnforcement_WrongRoleCantApprove` | Covered |
| 16 | Double-review prevention on already-approved gates | `PostgresGovernanceStore.cs:206-207` | `GovernanceLifecycleRuntimeTests.DoubleReview_AlreadyApproved_ThrowsInvalidOp` | Covered |
| 17 | Execution failure error persists across restart | `PostgresGovernanceStore.cs:293-342` | `GovernanceLifecycleRuntimeTests.ExecutionFailure_ErrorMessagePersistedAcrossRestart` | Covered |
| 18 | Multi-tenant governance isolation under concurrency | `PostgresGovernanceStore.cs` (tenant_id WHERE clause) | `GovernanceLifecycleRuntimeTests.ConcurrentTenants_IsolatedGovernanceOperations` | Covered |
| 19 | Approval policies persist and enforce across restart | `PostgresGovernanceStore.cs` | `GovernanceLifecycleRuntimeTests.PolicyPersistence_MultiplePolicies_SurviveRestart` | Covered |

| 20 | Interface-based inspection record/retrieve roundtrip | `Api/Security/InspectionService.cs`, `Core/Interfaces/IInspectionService.cs` | `InspectionPersistenceTests.RecordAndRetrieve_PolicyEvaluation_ViaInterface`, `RecordAndRetrieve_MemoryReference_ViaInterface`, `RecordAndRetrieve_WorkflowDiagnostics_ViaInterface` | Covered |
| 21 | Inspection tenant isolation via interface | `Api/Security/InspectionService.cs` | `InspectionPersistenceTests.RecordPolicyEvaluation_TenantIsolation` | Covered |
| 22 | GovernanceEventSubscriber wires to IInspectionService | `Api/Security/GovernanceEventSubscriber.cs` | `InspectionPersistenceTests.GovernanceEventSubscriber_RecordsInspection_ViaInterface`, `GovernanceEventSubscriber_RecordsMemoryReference_ViaInterface` | Covered |
| 23 | WorkflowFailureDiagnostics recorded on hero_workflow.failed | `Api/Security/GovernanceEventSubscriber.cs` | `InspectionPersistenceTests.GovernanceEventSubscriber_RecordsWorkflowDiagnostics_OnFailure` | Covered |
| 24 | In-memory inspection data volatile across instances | `Api/Security/InspectionService.cs` | `InspectionPersistenceTests.InMemoryData_LostAfterNewInstance` | Covered |
| 25 | RetentionSweepResult includes inspection rows | `Infrastructure/RetentionHostedService.cs` | `InspectionPersistenceTests.RetentionSweepResult_IncludesInspectionField` | Covered |

## Test Categories

All runtime-proof tests are tagged with `[Trait("Category", "RuntimeProof")]` for selective execution:

```bash
# Run all runtime proof tests
dotnet test --filter "Category=RuntimeProof"

# Run only database-backed proof tests (requires Docker for Testcontainers)
dotnet test --filter "Category=RuntimeProof&Database=PostgreSQL"

# Run only worker health proof tests (no Docker needed)
dotnet test --filter "Category=RuntimeProof&Subsystem=WorkerHealth"
```

## Infrastructure Requirements

| Test Suite | Requires Docker | Requires Network | Database |
|------------|----------------|-----------------|----------|
| WorkerHealthRuntimeTests | No | No | None |
| RetentionServiceRuntimeTests | Yes | No | PostgreSQL 16 (Testcontainers) |
| GovernanceLifecycleRuntimeTests | Yes | No | PostgreSQL 16 (Testcontainers) |

## Residual Proof Gaps

| Gap | Description | Difficulty | Notes |
|-----|-------------|-----------|-------|
| HTTP-level health endpoint test | Current tests verify probe logic but not the actual HTTP listener on port 8081 | Medium | Requires starting WorkerHealthService in-process; HttpListener needs elevated permissions on some OSes |
| Retention sweep session memory expiry | enterprise_memory session-layer retention is exercised by sweep code but not independently verified | Low | Table schema uses `layer = 'Session'` condition |
| NATS readiness check integration | Readiness probe's NATS connectivity check tested at model level, not with real NATS | Medium | Would require NATS Testcontainer |
| GovernanceKernel integration with Postgres | GovernanceKernel (policy engine, identity, security) tested in-memory; not yet wired to Postgres store in integration | High | Requires full DI wiring with mocked policy/identity/security engines |
| Retention sweep scheduling (02:00 UTC) | `TimeUntilNextRun()` logic not tested in isolation | Low | Pure function, easily unit-testable |

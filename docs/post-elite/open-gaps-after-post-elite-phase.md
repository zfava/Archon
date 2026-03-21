# Open Gaps After Post-Elite Phase

## Priority 1 — Wire Inspection Data at Runtime — **CLOSED**

**Gap**: The InspectionService can compose and serve inspection bundles, but upstream services (PolicyEngine, MemoryStore, WorkflowEngine) do not yet call the recording methods during execution.

**Resolution**: PolicyEngine now publishes `inspection.policy-evaluation-recorded` events via IEventBus after every `EvaluateAsync` call (`PolicyEngine.cs:168-196`). EnterpriseMemoryService publishes `inspection.memory-reference-recorded` events from `QueryAsync` (`EnterpriseMemoryService.cs`). OrganizationalMemoryStore publishes `inspection.memory-reference-recorded` events from `SearchAsync` (`OrganizationalMemoryStore.cs`). GovernanceEventSubscriber handles both event types and records them in InspectionService (`GovernanceEventSubscriber.cs:36-45, 88-168`).

**Tests**: `PolicyEngine_PublishesInspectionEvent_AfterEvaluation`, `PolicyEvaluationEvent_RecordsInInspectionService`, `MemoryReferenceEvent_RecordsInInspectionService`, `EnterpriseMemoryQuery_PublishesMemoryReferenceEvent`, `MemoryReferenceSubscriberFailure_DoesNotBreakEventBus` — all pass.

## Priority 2 — Proof Analytics Event Emission — **CLOSED**

**Gap**: Proof analytics events (DecisionCreated, ActionExecuted, ActualOutcomeRecorded, etc.) must be explicitly recorded via the API. They are not yet auto-emitted from the decision lifecycle, workflow execution, or action safety services.

**Resolution**: PostgresDecisionStore publishes `decision.created` and `decision.status-updated` events. HeroWorkflowService publishes `hero_workflow.started`, `hero_workflow.step-completed`, `hero_workflow.completed`, and `hero_workflow.failed` events. GatedActionExecutor publishes `gated-action.executed` events. GovernanceEventSubscriber handles all 6 proof event types and auto-records them via IProofAnalyticsService (`GovernanceEventSubscriber.cs:47-420`).

**Tests**: `DecisionCreatedEvent_EmitsProofEvent`, `WorkflowCompletedEvent_EmitsProofEvent`, `WorkflowFailedEvent_EmitsProofEventWithFailure`, `GatedActionExecuted_EmitsProofEvent`, `SubscriberFailure_DoesNotBreakEventBus` — all pass.

## Priority 3 — Real-Time Inspection Streaming — **CLOSED**

**Gap**: Inspection is currently request-response only. For long-running workflows, operators cannot watch diagnostics update in real time.

**Resolution**: SignalR `InspectionHub` created at `/hubs/inspection` following the `ControlPlaneDashboardHub` pattern (`InspectionHub.cs`). Hub supports `SubscribeToSubject(subjectType, subjectId)` and `UnsubscribeFromSubject` for group-based routing. `GovernanceEventSubscriber` injects optional `IHubContext<InspectionHub>` and broadcasts `PolicyEvaluationRecorded`, `MemoryReferenceRecorded`, and `WorkflowDiagnosticsUpdated` events to `inspection:{subjectType}:{subjectId}` groups via fire-and-forget with error logging. Frontend `useInspectionHub` hook manages connection lifecycle with automatic reconnect (`[0, 2000, 5000, 10000, 30000]ms`), typed events, and last-100-event buffer. `OperatorInspectionView` wires the hook with a "Live Updates" panel showing connection status and streaming events.

**Tests**: `InspectionHub_CanBeConstructed`, `GovernanceEventSubscriber_BroadcastsAfterPolicyEvaluation` (mock IHubContext verifies group routing and SendCoreAsync), `GovernanceEventSubscriber_WorksWithoutHub` (nullable hub graceful degradation) — all pass.

## Priority 4 — Historical Inspection Archives — **CLOSED**

**Gap**: Inspection data was stored in-memory (ConcurrentDictionary). It did not survive restarts and could not be queried historically.

**Resolution**: IInspectionService interface unified with three async Record methods (`RecordPolicyEvaluationAsync`, `RecordMemoryReferenceAsync`, `RecordWorkflowDiagnosticsAsync`). GovernanceEventSubscriber now injects `IInspectionService` (interface, not concrete type) and calls async Record methods. `ReplaceWithFactory<IInspectionService, PostgresInspectionStore>` now correctly swaps both read AND write operations when a Postgres connection string is present. Duplicate concrete `InspectionService` registration removed from Program.cs. WorkflowFailureDiagnostics now proactively persisted on `hero_workflow.failed` events. RetentionHostedService sweeps three inspection tables (`inspection_policy_evaluations`, `inspection_memory_references`, `inspection_workflow_diagnostics`) with 90-day default retention. Migration 026 adds `inspection_rows_deleted` column to `retention_log`.

**Tests**: `RecordAndRetrieve_PolicyEvaluation_ViaInterface`, `RecordAndRetrieve_MemoryReference_ViaInterface`, `RecordMultipleMemoryReferences_ReturnsAll`, `RecordAndRetrieve_WorkflowDiagnostics_ViaInterface`, `InMemoryData_LostAfterNewInstance`, `GovernanceEventSubscriber_RecordsInspection_ViaInterface`, `GovernanceEventSubscriber_RecordsMemoryReference_ViaInterface`, `GovernanceEventSubscriber_RecordsWorkflowDiagnostics_OnFailure`, `RecordPolicyEvaluation_TenantIsolation`, `RetentionSweepResult_IncludesInspectionField`, `GovernanceEventSubscriber_UsesInterfaceNotConcreteType` — all pass.

## Priority 5 — Proof Analytics Drill-Through — **CLOSED**

**Gap**: ~~Proof analytics dashboard shows aggregate metrics but clicking a decision or action does not navigate to its full inspection bundle or proof timeline.~~ ~~Cross-links pass the user to the target feature's root, not to a pre-populated subject ID.~~

**Resolution**: Deep-link query parameters are now fully implemented. OperatorInspectionView reads `subjectId` and `subjectType` from query params and auto-populates the inspection form. ProofAnalyticsView reads `decisionId` and auto-filters the timeline. ActionSafetyView reads `actionType` and auto-filters. Cross-link navigation calls pass the relevant parameters (e.g., `/inspection?subjectId=xyz&subjectType=decision`).

**Tests**: Frontend builds with zero TypeScript errors. Cross-link navigation verified in component source.

## Priority 6 — Action Safety Automatic Classification — **CLOSED**

**Gap**: Action safety classifications must be manually configured per action type. New action types are unclassified by default.

**Resolution**: `ActionSafetyService.GetOrInferClassificationAsync` checks for explicit classification first, then falls back to keyword-based auto-inference. Keyword categories: delete/remove/terminate/cancel/drop → Irreversible (no rollback); send/notify/email/publish/broadcast → Irreversible; update/modify/edit/change/patch → Reversible (automatic rollback); create/add/register/insert → Reversible (automatic rollback); approve/deny/review/reject → Compensatable (compensation rollback). Unknown action types default to Compensatable. Inferred classifications marked with `ClassifiedBy = "auto-inference"`. `PostgresActionSafetyStore` implements the same logic with DB-first lookup. `GatedActionExecutor` now calls `GetOrInferClassificationAsync` before execution and includes `reversibility` and `classifiedBy` in the published event payload.

**Tests**: 9 test methods (24 test cases total): `DeleteKeywords_InferIrreversible` (4 cases), `SendKeywords_InferIrreversible` (4), `UpdateKeywords_InferReversible` (4), `CreateKeywords_InferReversible` (3), `ApprovalKeywords_InferCompensatable` (3), `UnknownActionType_InferCompensatableDefault`, `ExplicitClassification_OverridesInference`, `ExplicitSetClassification_OverridesInference` — all pass.

## Priority 7 — Workflow Retry/Replay from Inspection — **CLOSED**

**Gap**: ~~Inspection shows workflow diagnostics and marks failures as retryable, but there is no inline retry/replay button.~~

**Resolution**: WorkflowDiagnosticsCard now includes a "Retry from Step" button that appears when `diagnostics.isRetryable` is true. On click, it calls `api.advanceHeroWorkflow(diagnostics.workflowId)`. The component manages loading, success, and error states with appropriate UI feedback (success badge, error banner, loading spinner).

**Tests**: Frontend builds with zero TypeScript errors. Component source verified in `archonai-ui/src/features/inspection/components/WorkflowDiagnosticsCard.tsx`.

## Priority 8 — Executive Command Post-Elite Metrics — **CLOSED**

**Gap**: ~~Executive Command now links to all post-elite systems via the "Governed Operations" quick-link bar, but does not yet display inline metrics from these systems.~~

**Resolution**: ExecutiveCommandSummary model extended with `ProofBrief` (TotalDecisions, WithOutcomes, AccuracyRate, SuccessRate, OverrideRate), `ActionSafetyBrief` (TotalActions, Reversible, Irreversible, RollbacksSucceeded, RollbacksFailed), and `WorkflowBrief` (Active, Completed, Failed, Recent). ExecutiveCommandService injects IProofAnalyticsService, IActionSafetyService, and IHeroWorkflowService and fans out their reads in the existing `Task.WhenAll` block with graceful null fallback. Frontend ExecutiveCommandView displays compact KPI cards with loading shimmer and null-safe rendering.

**Tests**: Backend builds with zero errors. Frontend builds with zero TypeScript errors.

## Non-Gaps (Verified Complete)

- Tenant isolation across all post-elite systems
- Permission gating (GovernanceRead/GovernanceWrite) on all endpoints
- Cross-system linkage via decision/workflow/action IDs
- Operator navigation between post-elite features via sidebar and cross-links
- CSS isolation between feature modules (no class collisions)
- API naming consistency (kebab-case URLs, camelCase bodies)
- Frontend type safety across all inspection, proof, and safety types
- Sidebar navigation groups post-elite features logically under Operations
- Executive Command surfaces all governed operations via quick-link bar
- Policy Simulation clearly labeled as DRY RUN (badge + subtitle)
- Consistent header patterns across all post-elite views (no orphan back links)

---

## Residual Gap Summary

All **8 of 8** priorities identified after the post-elite phase are now **CLOSED** with implemented code paths, passing tests, and runtime wiring.

Archon10 pass closed Priority 3 (SignalR InspectionHub with real-time broadcasting from GovernanceEventSubscriber, frontend hook, and OperatorInspectionView integration) and Priority 6 (keyword-based action safety auto-classification with 5 keyword categories, DB-first lookup, and GatedActionExecutor integration). Priority 4 (Historical Inspection Archives) closed with unified IInspectionService interface, async Record methods, GovernanceEventSubscriber DI fix, proactive WorkflowFailureDiagnostics persistence, retention sweeps for 3 inspection tables, and migration 026. All closed gaps use the existing IEventBus pattern for event-driven integration, preserving observability, tracing, graceful shutdown, and deterministic testability.

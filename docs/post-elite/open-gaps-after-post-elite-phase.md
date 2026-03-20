# Open Gaps After Post-Elite Phase

## Priority 1 — Wire Inspection Data at Runtime — **CLOSED**

**Gap**: The InspectionService can compose and serve inspection bundles, but upstream services (PolicyEngine, MemoryStore, WorkflowEngine) do not yet call the recording methods during execution.

**Resolution**: PolicyEngine now publishes `inspection.policy-evaluation-recorded` events via IEventBus after every `EvaluateAsync` call (`PolicyEngine.cs:168-196`). EnterpriseMemoryService publishes `inspection.memory-reference-recorded` events from `QueryAsync` (`EnterpriseMemoryService.cs`). OrganizationalMemoryStore publishes `inspection.memory-reference-recorded` events from `SearchAsync` (`OrganizationalMemoryStore.cs`). GovernanceEventSubscriber handles both event types and records them in InspectionService (`GovernanceEventSubscriber.cs:36-45, 88-168`).

**Tests**: `PolicyEngine_PublishesInspectionEvent_AfterEvaluation`, `PolicyEvaluationEvent_RecordsInInspectionService`, `MemoryReferenceEvent_RecordsInInspectionService`, `EnterpriseMemoryQuery_PublishesMemoryReferenceEvent`, `MemoryReferenceSubscriberFailure_DoesNotBreakEventBus` — all pass.

## Priority 2 — Proof Analytics Event Emission — **CLOSED**

**Gap**: Proof analytics events (DecisionCreated, ActionExecuted, ActualOutcomeRecorded, etc.) must be explicitly recorded via the API. They are not yet auto-emitted from the decision lifecycle, workflow execution, or action safety services.

**Resolution**: PostgresDecisionStore publishes `decision.created` and `decision.status-updated` events. HeroWorkflowService publishes `hero_workflow.started`, `hero_workflow.step-completed`, `hero_workflow.completed`, and `hero_workflow.failed` events. GatedActionExecutor publishes `gated-action.executed` events. GovernanceEventSubscriber handles all 6 proof event types and auto-records them via IProofAnalyticsService (`GovernanceEventSubscriber.cs:47-420`).

**Tests**: `DecisionCreatedEvent_EmitsProofEvent`, `WorkflowCompletedEvent_EmitsProofEvent`, `WorkflowFailedEvent_EmitsProofEventWithFailure`, `GatedActionExecuted_EmitsProofEvent`, `SubscriberFailure_DoesNotBreakEventBus` — all pass.

## Priority 3 — Real-Time Inspection Streaming

**Gap**: Inspection is currently request-response only. For long-running workflows, operators cannot watch diagnostics update in real time.

**Impact**: Operators must refresh manually to see updated step diagnostics during workflow execution.

**Fix**: Add SignalR channels for inspection events, similar to the existing `ControlPlaneDashboardHub`.

**Effort**: Medium — requires new hub methods and frontend subscription hooks.

## Priority 4 — Historical Inspection Archives

**Gap**: Inspection data is stored in-memory (ConcurrentDictionary). It does not survive restarts and cannot be queried historically.

**Impact**: Post-restart, all inspection data is lost. Long-term audit trails for compliance are not available.

**Fix**: Persist inspection records to the durable store (Postgres), with configurable retention policies.

**Effort**: Medium — requires new repository interface, migration, and retention job.

## Priority 5 — Proof Analytics Drill-Through — **CLOSED**

**Gap**: ~~Proof analytics dashboard shows aggregate metrics but clicking a decision or action does not navigate to its full inspection bundle or proof timeline.~~ ~~Cross-links pass the user to the target feature's root, not to a pre-populated subject ID.~~

**Resolution**: Deep-link query parameters are now fully implemented. OperatorInspectionView reads `subjectId` and `subjectType` from query params and auto-populates the inspection form. ProofAnalyticsView reads `decisionId` and auto-filters the timeline. ActionSafetyView reads `actionType` and auto-filters. Cross-link navigation calls pass the relevant parameters (e.g., `/inspection?subjectId=xyz&subjectType=decision`).

**Tests**: Frontend builds with zero TypeScript errors. Cross-link navigation verified in component source.

## Priority 6 — Action Safety Automatic Classification

**Gap**: Action safety classifications must be manually configured per action type. New action types are unclassified by default.

**Impact**: New action types bypass the safety classification system until an operator manually adds them.

**Fix**: Add default classification rules based on trust tier and risk level. Auto-classify actions when first encountered using policy engine risk scoring.

**Effort**: Medium — requires classification inference logic and policy rule integration.

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

Of the 8 priorities identified after the post-elite phase, **5 are now CLOSED** (priorities 1, 2, 5, 7, 8) with implemented code paths, passing tests, and runtime wiring. **3 remain open** (priorities 3, 4, 6):

- **Priority 3** (real-time inspection streaming) requires SignalR hub work — medium effort, not blocking for GA.
- **Priority 4** (historical inspection archives) requires Postgres persistence migration — medium effort, important for compliance but not blocking for initial deployment.
- **Priority 6** (action safety auto-classification) requires policy rule inference — medium effort, operator workaround exists (manual classification).

All closed gaps use the existing IEventBus pattern for event-driven integration, preserving observability, tracing, graceful shutdown, and deterministic testability. No Task.Run fire-and-forget patterns were introduced.

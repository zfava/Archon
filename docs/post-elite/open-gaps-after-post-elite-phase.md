# Open Gaps After Post-Elite Phase

## Priority 1 — Wire Inspection Data at Runtime

**Gap**: The InspectionService can compose and serve inspection bundles, but upstream services (PolicyEngine, MemoryStore, WorkflowEngine) do not yet call the recording methods during execution.

**Impact**: Inspection data for policy evaluations and memory references requires manual recording or is synthesized. Once wired, every policy evaluation and memory retrieval will automatically appear in inspection views.

**Fix**: Add calls to `InspectionService.RecordPolicyEvaluation()` in the PolicyEngine after each evaluation, and `RecordMemoryReference()` in the MemoryStore after each retrieval.

**Effort**: Low — 2 integration points.

## Priority 2 — Proof Analytics Event Emission

**Gap**: Proof analytics events (DecisionCreated, ActionExecuted, ActualOutcomeRecorded, etc.) must be explicitly recorded via the API. They are not yet auto-emitted from the decision lifecycle, workflow execution, or action safety services.

**Impact**: Proof analytics dashboards depend on events being recorded. Without automatic emission, operators must manually populate proof data.

**Fix**: Add event emission hooks in DecisionService (on status transitions), HeroWorkflowService (on step completion), and ActionSafetyService (on action recording and rollback).

**Effort**: Medium — 3-4 integration points with event bus wiring.

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

## Priority 5 — Proof Analytics Drill-Through (Partially Resolved)

**Gap**: ~~Proof analytics dashboard shows aggregate metrics but clicking a decision or action does not navigate to its full inspection bundle or proof timeline.~~ **Partially resolved**: Timeline detail view now includes cross-links to Inspection, Action Safety, and Simulation. PvA table rows navigate to decision timeline on click.

**Remaining**: Cross-links pass the user to the target feature's root, not to a pre-populated subject ID. Full deep-linking with query parameters (e.g., `/inspection?subjectId=xyz&subjectType=decision`) would eliminate the need for manual ID entry.

**Effort**: Low — frontend-only routing changes with query parameter parsing.

## Priority 6 — Action Safety Automatic Classification

**Gap**: Action safety classifications must be manually configured per action type. New action types are unclassified by default.

**Impact**: New action types bypass the safety classification system until an operator manually adds them.

**Fix**: Add default classification rules based on trust tier and risk level. Auto-classify actions when first encountered using policy engine risk scoring.

**Effort**: Medium — requires classification inference logic and policy rule integration.

## Priority 7 — Workflow Retry/Replay from Inspection

**Gap**: Inspection shows workflow diagnostics and marks failures as retryable, but there is no inline retry/replay button.

**Impact**: Operators must navigate to hero workflows view to retry. The inspection context is lost.

**Fix**: Add a "Retry from Step" action in the WorkflowDiagnosticsCard that calls the hero workflow advance/restart endpoint.

**Effort**: Low — frontend action button + API call.

## Priority 8 — Executive Command Post-Elite Metrics

**Gap**: Executive Command now links to all post-elite systems via the "Governed Operations" quick-link bar, but does not yet display inline metrics from these systems (e.g., proof accuracy rate, active workflow count, rollback eligibility count, simulation run count).

**Impact**: Executives must navigate to each post-elite feature individually to see operational metrics. The executive summary would be stronger with inline KPIs from these systems.

**Fix**: Extend the `/executive-command/summary` API to include `proofBrief`, `actionSafetyBrief`, and `workflowBrief` sub-objects. Display as additional signal cards or section summaries.

**Effort**: Medium — backend aggregation + frontend cards.

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

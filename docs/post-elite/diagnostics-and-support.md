# Diagnostics & Support

## Purpose

This document describes the diagnostics and support capabilities built into the ArchonAI operator inspection tooling, enabling support teams and operators to efficiently debug, understand, and resolve platform issues.

## Diagnostic Capabilities

### 1. Decision Diagnostics

**What operators can inspect:**
- Why a decision was created (objective, domain, constraints)
- What assumptions were used (explicit assumption list)
- Which alternatives were considered (with pros/cons/confidence/value)
- Why a specific option was recommended (rationale text, confidence score)
- How the recommendation changed over time (lifecycle event history)
- What policy rules were applied (rule-by-rule breakdown)
- What memory/context influenced the decision (source, relevance, usage)

**API**: `GET /api/v1/inspection/decisions/{id}/rationale`

### 2. Policy Diagnostics

**What operators can inspect:**
- Whether an action was allowed or denied
- Full risk score breakdown
- Each policy rule evaluated (name, category, pass/fail, risk contribution)
- Guardrail violations that triggered denial
- Approval state (not-required, pending, approved, checkpoint-pending)
- Manual override state (none, allow, deny)
- Confidence score and threshold comparison

**API**: `GET /api/v1/inspection/policy/{subjectType}/{subjectId}`

### 3. Memory/Context Diagnostics

**What operators can inspect:**
- Which memory records were retrieved for a decision/action
- Memory type (Strategy, Outcome, Pattern, Lesson)
- Source system (org-memory, knowledge-graph, enterprise-memory)
- Relevance score (how closely the memory matched the query)
- Usage context (how the memory was applied)

**API**: `GET /api/v1/inspection/memory/{subjectType}/{subjectId}`

### 4. Workflow Failure Diagnostics

**What operators can inspect:**
- Current workflow state (Failed, Stalled, Cancelled, etc.)
- Failure category (step-failure, stalled, cancelled, unknown)
- Root cause (error message from failed step)
- Step-by-step execution diagnostics (status, duration, errors per step)
- Whether the workflow is retryable
- Suggested remediation actions
- Related exceptions from the exception intelligence center

**API**: `GET /api/v1/inspection/workflows/{id}/diagnostics`

## Support Workflows

### Investigating a Failed Decision

1. Navigate to `/inspection` in the operator UI
2. Filter by `subjectType=decision` and the relevant domain
3. Click the failed/rejected decision
4. Review the rationale bundle:
   - Check assumptions — were they valid?
   - Check policy evaluation — was it correctly gated?
   - Check memory references — was relevant context available?
   - Check change history — did the recommendation shift?

### Investigating a Stalled Workflow

1. Navigate to `/inspection` → Workflow Diagnostics tab
2. Enter the workflow ID
3. Review step diagnostics:
   - Which step is stuck?
   - What agent type was assigned?
   - Is the error transient or permanent?
4. Check suggested remediation
5. If retryable, use the hero workflows UI to retry

### Investigating a Policy Denial

1. Navigate to `/inspection` → Policy Inspection tab
2. Enter the subject type and ID
3. Review rule-by-rule evaluation:
   - Which rules failed?
   - What was the risk contribution?
   - Was a manual override applied?
4. If the denial was incorrect, use the overrides system to adjust

## Operator Trust Model

The inspection tooling is designed to build operator trust through:

1. **Transparency**: Every decision is explainable with full rationale
2. **Traceability**: Policy evaluations show rule-by-rule reasoning
3. **Debuggability**: Workflow failures include step-level diagnostics
4. **Context visibility**: Memory sources are explicitly listed
5. **Change tracking**: Recommendation changes are logged
6. **Safe boundaries**: Tenant isolation prevents cross-org data leakage
7. **Permission gating**: Only governance-authorized operators can inspect

## Limitations & Future Work

- Memory reference recording requires explicit integration by upstream services
- Workflow diagnostics for non-hero-workflow types require additional integration
- Real-time inspection (streaming diagnostics) is not yet supported
- Historical inspection archives (older than memory retention) are not yet available

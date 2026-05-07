# Hero Workflows

## Overview

Hero workflows are pre-built, end-to-end governed business processes that compose ArchonAI's elite subsystems into a single orchestrated lifecycle. Each workflow demonstrates the full value chain: decision creation, consequence modeling, trust-tier evaluation, approval gating, execution, outcome capture, and institutional memory.

## Architecture

### Composition Model

A hero workflow is defined by a `HeroWorkflowDefinition` containing an ordered list of `HeroStepDefinition` entries. Each step maps to one of ArchonAI's elite subsystems:

| Subsystem | Step Type | What It Does |
|---|---|---|
| Decision Engine | `decision` | Creates a first-class decision record with alternatives and risk assessment |
| Financial Consequence Engine | `financial-consequence` | Attaches revenue/cost impact ranges, ROI estimates, downside risk |
| Trust-Tiered Autonomy | `trust-tier` | Evaluates whether the action can auto-execute or requires human approval |
| Governance / Approvals | `approval` | Routes through approval gate; skips if trust tier allows auto-execution |
| Decision Engine + Outcomes | `execution` | Transitions decision to Executing, records expected outcome |
| Outcome Learning | `outcome` | Records actual outcome, computes variance for calibration |
| Exception Intelligence | `exception` | Raises an operational exception (compliance workflows) |
| Enterprise Memory | `memory` | Stores institutional memory for future reference |

### Lifecycle

```
Draft → InProgress → AwaitingApproval → Executing → Completed
                                                  ↘ Failed
                                                  ↘ Cancelled
```

Each step transitions through: `Pending → InProgress → Completed | Failed | Skipped`.

When a workflow starts, the first step is automatically executed. Subsequent steps are advanced via the `POST /hero-workflows/{id}/advance` endpoint.

### Artifact Tracking

As each step executes, it produces artifacts stored in the workflow instance's `Artifacts` dictionary:

| Key | Source Step | Description |
|---|---|---|
| `decisionId` | decision | The created decision record ID |
| `consequenceId` | financial-consequence | The financial consequence model ID |
| `trustDisposition` | trust-tier | The trust tier evaluation result |
| `effectiveTier` | trust-tier | The effective execution tier |
| `approvalGateId` | approval | The governance approval gate ID |
| `approvalStatus` | approval | Approval outcome (Approved/auto_approved) |
| `outcomeId` | execution | The expected outcome record ID |
| `exceptionId` | exception | The raised exception ID (compliance workflows) |

## API

### Endpoints

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/api/v1/hero-workflows/catalog` | `GovernanceRead` | List all workflow definitions |
| `GET` | `/api/v1/hero-workflows/catalog/{type}` | `GovernanceRead` | Get a specific definition |
| `POST` | `/api/v1/hero-workflows` | `GovernanceWrite` | Start a new workflow instance |
| `POST` | `/api/v1/hero-workflows/{id}/advance` | `GovernanceWrite` | Advance to the next step |
| `GET` | `/api/v1/hero-workflows/{id}` | `GovernanceRead` | Get a workflow instance |
| `GET` | `/api/v1/hero-workflows` | `GovernanceRead` | List instances (filterable) |
| `POST` | `/api/v1/hero-workflows/{id}/cancel` | `GovernanceWrite` | Cancel a workflow |

### Starting a Workflow

```json
POST /api/v1/hero-workflows
{
  "workflowType": "vendor-selection",
  "title": "Q1 Cloud Provider Evaluation",
  "inputs": {
    "decisionTitle": "Select cloud infrastructure provider",
    "domain": "procurement",
    "riskLevel": "High",
    "expectedValue": "500000"
  }
}
```

### Advancing a Workflow

```json
POST /api/v1/hero-workflows/{id}/advance
{
  "inputs": {
    "actualOutcome": "Vendor delivered 15% under budget",
    "actualValue": "425000"
  }
}
```

## Frontend

The hero workflows view (`/hero-workflows`) provides:

1. **Catalog tab** — Browse available workflow definitions, see step composition, start new instances
2. **Instances tab** — View running and completed workflows with progress tracking
3. **Detail view** — Step-by-step timeline with status indicators, artifact inspection, advance/cancel controls

## Test Coverage

23 integration tests covering:

- Catalog retrieval (3 workflow definitions, correct categories)
- Lifecycle progression (start, advance through all steps, completion)
- Tenant isolation (cross-tenant access blocked for get, list, advance, cancel)
- Artifact creation verification (decision, consequence, outcome stored in downstream services)
- Approval/trust-tier linkage (governance gate creation, disposition recording)
- Cancel behavior (status transition, no further advancement)
- Error handling (invalid workflow type throws)
- List filtering (by workflow type, step count accuracy)

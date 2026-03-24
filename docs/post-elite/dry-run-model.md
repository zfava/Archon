# Dry-Run Model

## Purpose

The dry-run model defines how ArchonAI simulates governed actions without side effects. This document specifies the safety guarantees, the read-only contract, and the mapping between simulation and execution.

## Safety Guarantees

### What Simulation Does NOT Do

| Operation | Simulation Behavior |
|---|---|
| Create decisions | Never — `SimulatedDecision` is a projection only |
| Create financial consequences | Never — `SimulatedEconomicEffect` is computed locally |
| Request approvals | Never — `SimulatedApproval` indicates what *would* be required |
| Publish events | Never — no `IEventBus.PublishAsync` calls |
| Store enterprise memory | Never — no memory records created |
| Raise exceptions | Never — no exception records created |
| Start workflows | Never — `SimulatedWorkflowPreview` reads definitions only |
| Mutate trust-tier state | Never — `EvaluateAsync` is read-only |

### Read-Only Service Methods Used

| Service | Method | Why It's Safe |
|---|---|---|
| `ITrustTierService` | `EvaluateAsync` | Evaluates against existing policies; does not store results |
| `IGovernanceService` | `RequiresApprovalAsync` | Checks policy configuration; pure read |
| `IGovernanceService` | `ListApprovalPoliciesAsync` | Lists existing policies; pure read |
| `IHeroWorkflowService` | `GetDefinitionAsync` | Returns static workflow definition; pure read |

### Verification

Integration tests explicitly verify zero side effects:
- Decision service contains no records after simulation
- Financial consequence service contains no records after simulation
- Event bus receives no publish calls during simulation
- Governance service has no pending approval gates after simulation

## Simulation-to-Execution Mapping

When an operator decides to proceed after reviewing a simulation, the actual execution path follows the same logic but with real writes:

| Simulation Output | Execution Equivalent |
|---|---|
| `SimulatedDecision` | `DecisionRecord` created via `IDecisionService.CreateAsync` |
| `SimulatedEconomicEffect` | `FinancialConsequence` attached via `IFinancialConsequenceService.AttachAsync` |
| `TrustTierEvaluation` | Same evaluation drives real execution gating |
| `SimulatedApproval` | `ApprovalGate` created via `IGovernanceService.RequestApprovalAsync` |
| `SimulatedWorkflowPreview` | `HeroWorkflowInstance` started via `IHeroWorkflowService.StartAsync` |

## Verdicts

| Verdict | Meaning | Operator Action |
|---|---|---|
| **Allowed** | Action would auto-execute without intervention | Safe to proceed |
| **RequiresApproval** | Action requires human approval before execution | Route for approval |
| **Blocked** | Action is blocked by trust-tier policy | Cannot proceed without policy change |
| **RecommendOnly** | System would recommend but not execute | Manual execution required |
| **ObserveOnly** | System would observe and log only | Informational only |

## Policy Outcome Model

Each simulation evaluates three policy dimensions:

### 1. Risk Level Policy
- Compares the action's risk level against the auto-approval threshold (< High)
- **Passes** when risk is Low or Medium
- **Fails** when risk is High or Critical

### 2. Trust Tier Policy
- Evaluates whether the action is allowed at the requested trust tier
- Uses tenant-configured trust policies and confidence/value thresholds
- Reports disposition (AutoExecute, DraftForApproval, Recommend, Observe) and effective tier

### 3. Approval Policy
- Checks governance approval policies for the action type
- Also considers trust-tier disposition and risk-level requirements
- Reports the matched policy, required approver role, and separation-of-duties requirement

## Economic Projection

When financial parameters are provided, the simulation computes:

```
Net Impact Low  = Revenue Impact Low  - Cost Impact High
Net Impact High = Revenue Impact High - Cost Impact Low
```

This conservative approach shows the worst-case net impact as the lower bound and the best-case as the upper bound.

## Storage

Simulation results are stored in memory (`ConcurrentDictionary`) for retrieval and historical comparison. Results are tenant-scoped and access-controlled.

## Constraints

- Simulation results do not persist across application restarts (in-memory storage)
- Simulation uses the governance configuration at the time of simulation; policy changes after simulation are not reflected
- Workflow preview shows projected outcomes based on current trust evaluation; actual step outcomes may differ at execution time

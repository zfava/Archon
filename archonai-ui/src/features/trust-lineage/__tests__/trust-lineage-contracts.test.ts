import { describe, it, expect } from 'vitest';
import type {
  TrustLineageResponse,
  TrustPostureResponse,
  ApprovalGate,
  GovernedAction,
  ProofEvent,
  LineageSummary,
  LineageOutcome,
} from '../trust-lineage.types';

// ── Fixture Factories ──

function makeLineageResponse(overrides?: Partial<TrustLineageResponse>): TrustLineageResponse {
  return {
    decisionId: 'dec-001',
    decision: {
      title: 'Expand APAC region',
      domain: 'Growth',
      objective: 'Increase revenue',
      reversibility: 'Compensatable',
      riskLevel: 'Medium',
      confidence: 0.82,
      expectedValue: 500_000,
      status: 'Approved',
      createdBy: 'alice@acme.com',
      createdAtUtc: '2026-01-15T10:00:00Z',
      requiresApproval: true,
    },
    approvalGates: [],
    governedActions: [],
    outcome: null,
    proofTimeline: null,
    statusHistory: [],
    lineageSummary: {
      hasApproval: false,
      hasExecution: false,
      hasOutcome: false,
      hasProofTrail: false,
      allActionsReversible: true,
      anyRollbackAttempted: false,
      varianceWithinThreshold: true,
    },
    ...overrides,
  };
}

function makeApprovalGate(overrides?: Partial<ApprovalGate>): ApprovalGate {
  return {
    id: 'gate-001',
    actionType: 'DeployService',
    resourceId: 'svc-apac',
    requestedBy: 'alice@acme.com',
    justification: 'Critical for Q1 target',
    status: 'Approved',
    reviewedBy: 'bob@acme.com',
    reviewNotes: 'LGTM',
    requestedAtUtc: '2026-01-15T10:30:00Z',
    reviewedAtUtc: '2026-01-15T11:00:00Z',
    executionStatus: 'Succeeded',
    executionError: null,
    executedAtUtc: '2026-01-15T11:05:00Z',
    ...overrides,
  };
}

function makeGovernedAction(overrides?: Partial<GovernedAction>): GovernedAction {
  return {
    id: 'action-001',
    actionType: 'DeployService',
    description: 'Deploy APAC service instance',
    status: 'Succeeded',
    executedBy: 'deployer@acme.com',
    executedAtUtc: '2026-01-15T11:05:00Z',
    safety: {
      reversibility: 'Reversible',
      rollbackSupported: true,
      rollbackStrategy: 'BlueGreen',
      rollbackWindow: 60,
      safetySummary: 'Blue-green deployment with instant rollback',
    },
    rollbackAttempts: 0,
    compensationOutcome: null,
    approvalGateId: 'gate-001',
    ...overrides,
  };
}

function makePostureResponse(overrides?: Partial<TrustPostureResponse>): TrustPostureResponse {
  return {
    tenantId: 'tenant-001',
    generatedAtUtc: '2026-01-15T12:00:00Z',
    governance: {
      activePolicies: 5,
      totalPolicies: 8,
      pendingApprovals: 2,
      recentApprovals: { total: 20, approved: 18, denied: 2, approvalRate: 0.9 },
      separationOfDutiesEnforced: true,
    },
    safety: {
      totalActions: 100,
      reversible: 70,
      compensatable: 20,
      irreversible: 10,
      reversibilityRate: 0.9,
      rollbacksAttempted: 5,
      rollbacksSucceeded: 4,
      rollbacksFailed: 1,
      rollbackSuccessRate: 0.8,
      withinRollbackWindow: 4,
      windowExpired: 1,
    },
    trustTiers: {
      totalPolicies: 3,
      enabledPolicies: 2,
      actionsCovered: 5,
      requiresReversible: 1,
    },
    outcomes: null,
    proofAnalytics: null,
    ...overrides,
  };
}

// ── Type-Safe Data Shape Tests ──

describe('TrustLineageResponse contract', () => {
  it('accepts a minimal valid lineage response', () => {
    const response = makeLineageResponse();
    expect(response.decisionId).toBe('dec-001');
    expect(response.decision.title).toBe('Expand APAC region');
    expect(response.approvalGates).toHaveLength(0);
    expect(response.governedActions).toHaveLength(0);
    expect(response.outcome).toBeNull();
    expect(response.proofTimeline).toBeNull();
  });

  it('accepts a full lineage response with all sections populated', () => {
    const gate = makeApprovalGate();
    const action = makeGovernedAction();
    const outcome: LineageOutcome = {
      expectedOutcomeSummary: 'Revenue increase',
      expectedValue: 500_000,
      confidenceAtPrediction: 0.82,
      actualOutcomeSummary: 'Revenue increased above target',
      actualValue: 550_000,
      valueVariance: 50_000,
      variancePercent: 10.0,
      direction: 'Positive',
      assessment: 'Accurate',
      recalibrationSignal: null,
      createdAtUtc: '2026-01-15T10:00:00Z',
      updatedAtUtc: '2026-02-15T10:00:00Z',
    };
    const proofEvent: ProofEvent = {
      eventType: 'ApprovalGranted',
      actor: 'bob@acme.com',
      detail: 'Approved deployment',
      expectedValue: null,
      actualValue: null,
      isSuccess: true,
      overrideReason: null,
      economicImpact: null,
      occurredAtUtc: '2026-01-15T11:00:00Z',
    };

    const response = makeLineageResponse({
      approvalGates: [gate],
      governedActions: [action],
      outcome,
      proofTimeline: {
        decisionTitle: 'Expand APAC region',
        domain: 'Growth',
        totalEvents: 1,
        events: [proofEvent],
        summary: 'All phases complete',
      },
      lineageSummary: {
        hasApproval: true,
        hasExecution: true,
        hasOutcome: true,
        hasProofTrail: true,
        allActionsReversible: true,
        anyRollbackAttempted: false,
        varianceWithinThreshold: true,
      },
    });

    expect(response.approvalGates).toHaveLength(1);
    expect(response.approvalGates[0].status).toBe('Approved');
    expect(response.governedActions[0].safety.reversibility).toBe('Reversible');
    expect(response.outcome?.assessment).toBe('Accurate');
    expect(response.proofTimeline?.events[0].eventType).toBe('ApprovalGranted');
    expect(response.lineageSummary.hasApproval).toBe(true);
  });

  it('enforces reversibility level values', () => {
    const action = makeGovernedAction({ safety: { ...makeGovernedAction().safety, reversibility: 'Irreversible' } });
    expect(action.safety.reversibility).toBe('Irreversible');
  });

  it('handles null optional fields in outcome', () => {
    const outcome: LineageOutcome = {
      expectedOutcomeSummary: null,
      expectedValue: null,
      confidenceAtPrediction: 0.5,
      actualOutcomeSummary: null,
      actualValue: null,
      valueVariance: null,
      variancePercent: null,
      direction: 'Neutral',
      assessment: 'Accurate',
      recalibrationSignal: null,
      createdAtUtc: '2026-01-15T10:00:00Z',
      updatedAtUtc: null,
    };
    expect(outcome.expectedValue).toBeNull();
    expect(outcome.actualValue).toBeNull();
    expect(outcome.valueVariance).toBeNull();
  });
});

describe('TrustPostureResponse contract', () => {
  it('accepts a valid posture response', () => {
    const posture = makePostureResponse();
    expect(posture.governance.activePolicies).toBe(5);
    expect(posture.safety.reversibilityRate).toBe(0.9);
    expect(posture.trustTiers.enabledPolicies).toBe(2);
  });

  it('computes approval rate consistency', () => {
    const posture = makePostureResponse();
    const { approved, total } = posture.governance.recentApprovals;
    expect(posture.governance.recentApprovals.approvalRate).toBeCloseTo(approved / total, 1);
  });

  it('validates safety counts sum correctly', () => {
    const posture = makePostureResponse();
    const { reversible, compensatable, irreversible, totalActions } = posture.safety;
    expect(reversible + compensatable + irreversible).toBe(totalActions);
  });
});

describe('LineageSummary contract', () => {
  it('reflects all-phases-complete state', () => {
    const summary: LineageSummary = {
      hasApproval: true,
      hasExecution: true,
      hasOutcome: true,
      hasProofTrail: true,
      allActionsReversible: true,
      anyRollbackAttempted: false,
      varianceWithinThreshold: true,
    };
    expect(Object.values(summary).filter(v => v === true)).toHaveLength(6);
    expect(summary.anyRollbackAttempted).toBe(false);
  });

  it('reflects incomplete lineage state', () => {
    const summary: LineageSummary = {
      hasApproval: true,
      hasExecution: false,
      hasOutcome: false,
      hasProofTrail: false,
      allActionsReversible: true,
      anyRollbackAttempted: false,
      varianceWithinThreshold: true,
    };
    expect(summary.hasExecution).toBe(false);
    expect(summary.hasOutcome).toBe(false);
  });
});

describe('ApprovalGate contract', () => {
  it('handles pending gate with no review', () => {
    const gate = makeApprovalGate({
      status: 'Pending',
      reviewedBy: null,
      reviewNotes: null,
      reviewedAtUtc: null,
      executionStatus: 'NotExecuted',
      executedAtUtc: null,
    });
    expect(gate.status).toBe('Pending');
    expect(gate.reviewedBy).toBeNull();
    expect(gate.executionStatus).toBe('NotExecuted');
  });

  it('handles denied gate with notes', () => {
    const gate = makeApprovalGate({
      status: 'Denied',
      reviewNotes: 'Too risky for current quarter',
      executionStatus: 'NotExecuted',
    });
    expect(gate.status).toBe('Denied');
    expect(gate.reviewNotes).toBe('Too risky for current quarter');
  });
});

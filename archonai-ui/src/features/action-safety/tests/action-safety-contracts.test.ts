import { describe, it, expect } from 'vitest';
import type {
  ActionSafetyClassification,
  GovernedActionRecord,
  RollbackAttempt,
} from '../types';

// ── Fixture Factories ──

function makeClassification(overrides?: Partial<ActionSafetyClassification>): ActionSafetyClassification {
  return {
    id: 'cls-001',
    actionType: 'DeployService',
    reversibility: 'Reversible',
    rollbackSupported: true,
    rollbackStrategy: 'Automatic',
    rollbackWindow: '60m',
    compensationDescription: null,
    operatorNotes: null,
    classifiedBy: 'safety-engine',
    classifiedAtUtc: '2026-01-15T10:00:00Z',
    safetySummary: 'Blue-green deployment with instant rollback',
    ...overrides,
  };
}

function makeRollbackAttempt(overrides?: Partial<RollbackAttempt>): RollbackAttempt {
  return {
    id: 'rb-001',
    actionId: 'action-001',
    initiatedBy: 'ops@acme.com',
    status: 'InProgress',
    detail: 'Rolling back deployment',
    error: null,
    initiatedAtUtc: '2026-01-15T11:00:00Z',
    completedAtUtc: null,
    ...overrides,
  };
}

function makeGovernedAction(overrides?: Partial<GovernedActionRecord>): GovernedActionRecord {
  return {
    id: 'action-001',
    tenantId: 'tenant-001',
    decisionId: 'dec-001',
    workflowId: null,
    approvalGateId: 'gate-001',
    actionType: 'DeployService',
    description: 'Deploy APAC service instance',
    safetyClassification: makeClassification(),
    status: 'Succeeded',
    executedBy: 'deployer@acme.com',
    executedAtUtc: '2026-01-15T11:05:00Z',
    rollbackHistory: [],
    compensationOutcome: null,
    updatedAtUtc: '2026-01-15T11:05:00Z',
    ...overrides,
  };
}

// ── Tests ──

describe('ActionSafetyClassification contract', () => {
  it('has correct reversibility values', () => {
    const reversible = makeClassification({ reversibility: 'Reversible' });
    expect(reversible.reversibility).toBe('Reversible');

    const compensatable = makeClassification({ reversibility: 'Compensatable' });
    expect(compensatable.reversibility).toBe('Compensatable');

    const irreversible = makeClassification({ reversibility: 'Irreversible' });
    expect(irreversible.reversibility).toBe('Irreversible');
  });

  it('safety summary is never empty string', () => {
    const cls = makeClassification();
    expect(cls.safetySummary.length).toBeGreaterThan(0);
  });
});

describe('RollbackAttempt contract', () => {
  it('transitions from InProgress to Succeeded', () => {
    const attempt = makeRollbackAttempt({ status: 'Succeeded', completedAtUtc: '2026-01-15T11:05:00Z' });
    expect(attempt.status).toBe('Succeeded');
    expect(attempt.completedAtUtc).not.toBeNull();
  });

  it('transitions from InProgress to Failed', () => {
    const attempt = makeRollbackAttempt({
      status: 'Failed',
      error: 'Timeout during rollback',
      completedAtUtc: '2026-01-15T11:10:00Z',
    });
    expect(attempt.status).toBe('Failed');
    expect(attempt.error).toBe('Timeout during rollback');
  });

  it('transitions from InProgress to Blocked', () => {
    const attempt = makeRollbackAttempt({
      status: 'Blocked',
      detail: 'Blocked by active dependency',
    });
    expect(attempt.status).toBe('Blocked');
    expect(attempt.detail).toBe('Blocked by active dependency');
  });
});

describe('GovernedActionRecord contract', () => {
  it('accepts empty rollback history', () => {
    const action = makeGovernedAction({ rollbackHistory: [] });
    expect(action.rollbackHistory).toHaveLength(0);
  });

  it('accepts multiple rollback attempts', () => {
    const action = makeGovernedAction({
      rollbackHistory: [
        makeRollbackAttempt({ id: 'rb-001', status: 'Failed', error: 'First attempt failed' }),
        makeRollbackAttempt({ id: 'rb-002', status: 'Succeeded', completedAtUtc: '2026-01-15T12:00:00Z' }),
      ],
    });
    expect(action.rollbackHistory).toHaveLength(2);
    expect(action.rollbackHistory[0].status).toBe('Failed');
    expect(action.rollbackHistory[1].status).toBe('Succeeded');
  });
});

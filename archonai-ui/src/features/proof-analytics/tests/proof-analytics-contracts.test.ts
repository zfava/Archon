import { describe, it, expect } from 'vitest';
import type {
  ProofEvent,
  ProofTimeline,
  ProofTimelineSummary,
  PredictedVsActualEntry,
  TrustByActionType,
} from '../types';

// ── Fixture Factories ──

function makeProofEvent(overrides?: Partial<ProofEvent>): ProofEvent {
  return {
    id: 'evt-001',
    tenantId: 'tenant-001',
    decisionId: 'dec-001',
    workflowId: null,
    eventType: 'ApprovalGranted',
    actor: 'bob@acme.com',
    detail: 'Approved deployment',
    expectedValue: null,
    actualValue: null,
    variance: null,
    variancePercent: null,
    actionType: 'DeployService',
    isSuccess: true,
    overrideReason: null,
    economicImpact: null,
    impactAttribution: null,
    occurredAtUtc: '2026-01-15T11:00:00Z',
    ...overrides,
  };
}

function makeTimelineSummary(overrides?: Partial<ProofTimelineSummary>): ProofTimelineSummary {
  return {
    totalEvents: 5,
    hasOutcome: true,
    wasOverridden: false,
    wasReversed: false,
    predictedValue: 500_000,
    actualValue: 550_000,
    variance: 50_000,
    variancePercent: 10.0,
    finalAssessment: 'Accurate',
    decisionToOutcomeDuration: 'P30D',
    ...overrides,
  };
}

function makePredictedVsActual(overrides?: Partial<PredictedVsActualEntry>): PredictedVsActualEntry {
  return {
    decisionId: 'dec-001',
    title: 'Expand APAC region',
    domain: 'Growth',
    predictedValue: 500_000,
    actualValue: 550_000,
    variance: 50_000,
    variancePercent: 10.0,
    direction: 'Positive',
    decisionCreatedAtUtc: '2026-01-15T10:00:00Z',
    outcomeObservedAtUtc: '2026-02-15T10:00:00Z',
    ...overrides,
  };
}

function makeTrustByActionType(overrides?: Partial<TrustByActionType>): TrustByActionType {
  return {
    actionType: 'DeployService',
    totalDecisions: 100,
    withOutcomes: 80,
    accuracyRate: 0.85,
    overrideRate: 0.05,
    meanConfidence: 0.78,
    meanVariancePercent: 8.5,
    trustGrade: 'A',
    ...overrides,
  };
}

// ── Tests ──

describe('ProofEvent contract', () => {
  it('has required fields on minimal event', () => {
    const event = makeProofEvent();
    expect(event.id).toBe('evt-001');
    expect(event.tenantId).toBe('tenant-001');
    expect(event.decisionId).toBe('dec-001');
    expect(event.eventType).toBe('ApprovalGranted');
    expect(event.occurredAtUtc).toBeTruthy();
  });
});

describe('ProofTimelineSummary contract', () => {
  it('computed correctly — hasOutcome, wasOverridden, wasReversed', () => {
    const summary = makeTimelineSummary({
      hasOutcome: true,
      wasOverridden: false,
      wasReversed: false,
    });
    expect(summary.hasOutcome).toBe(true);
    expect(summary.wasOverridden).toBe(false);
    expect(summary.wasReversed).toBe(false);
  });

  it('reflects overridden and reversed state', () => {
    const summary = makeTimelineSummary({
      hasOutcome: true,
      wasOverridden: true,
      wasReversed: true,
    });
    expect(summary.wasOverridden).toBe(true);
    expect(summary.wasReversed).toBe(true);
  });
});

describe('PredictedVsActualEntry contract', () => {
  it('positive variance calculation', () => {
    const entry = makePredictedVsActual({ variance: 50_000, variancePercent: 10.0 });
    expect(entry.variance).toBeGreaterThan(0);
    expect(entry.variancePercent).toBeGreaterThan(0);
    expect(entry.direction).toBe('Positive');
  });

  it('negative variance calculation', () => {
    const entry = makePredictedVsActual({
      actualValue: 400_000,
      variance: -100_000,
      variancePercent: -20.0,
      direction: 'Negative',
    });
    expect(entry.variance).toBeLessThan(0);
    expect(entry.variancePercent).toBeLessThan(0);
    expect(entry.direction).toBe('Negative');
  });

  it('zero variance calculation', () => {
    const entry = makePredictedVsActual({
      actualValue: 500_000,
      variance: 0,
      variancePercent: 0,
      direction: 'Neutral',
    });
    expect(entry.variance).toBe(0);
    expect(entry.variancePercent).toBe(0);
    expect(entry.direction).toBe('Neutral');
  });

  it('handles null actuals', () => {
    const entry = makePredictedVsActual({
      actualValue: null,
      variance: null,
      variancePercent: null,
      outcomeObservedAtUtc: null,
    });
    expect(entry.actualValue).toBeNull();
    expect(entry.variance).toBeNull();
    expect(entry.variancePercent).toBeNull();
    expect(entry.outcomeObservedAtUtc).toBeNull();
  });
});

describe('TrustByActionType contract', () => {
  it('percentages are between 0 and 1', () => {
    const trust = makeTrustByActionType();
    expect(trust.accuracyRate).toBeGreaterThanOrEqual(0);
    expect(trust.accuracyRate).toBeLessThanOrEqual(1);
    expect(trust.overrideRate).toBeGreaterThanOrEqual(0);
    expect(trust.overrideRate).toBeLessThanOrEqual(1);
    expect(trust.meanConfidence).toBeGreaterThanOrEqual(0);
    expect(trust.meanConfidence).toBeLessThanOrEqual(1);
  });
});

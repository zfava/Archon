import { describe, it, expect } from 'vitest';
import type {
  PolicyEvaluationResult,
  MemoryContextReference,
  PolicyRuleResult,
} from '../types';

// ── Fixture Factories ──

function makePolicyRule(overrides?: Partial<PolicyRuleResult>): PolicyRuleResult {
  return {
    ruleName: 'RequireApproval',
    ruleCategory: 'governance',
    passed: true,
    riskContribution: 0,
    detail: 'Action type requires approval gate',
    ...overrides,
  };
}

function makePolicyEvaluation(overrides?: Partial<PolicyEvaluationResult>): PolicyEvaluationResult {
  return {
    evaluationId: 'eval-001',
    tenantId: 'tenant-001',
    subjectType: 'Decision',
    subjectId: 'dec-001',
    isAllowed: true,
    riskScore: 25,
    confidenceScore: 0.92,
    requiresApproval: true,
    approvalState: 'Approved',
    manualOverrideState: 'None',
    approvalCheckpoint: 'PreExecution',
    guardrailViolations: [],
    rulesEvaluated: [makePolicyRule()],
    reason: 'All policy rules passed',
    evaluatedAtUtc: '2026-01-15T10:00:00Z',
    ...overrides,
  };
}

function makeMemoryContextRef(overrides?: Partial<MemoryContextReference>): MemoryContextReference {
  return {
    memoryId: 'mem-001',
    memoryType: 'episodic',
    source: 'memory-store',
    contentSummary: 'Previous APAC expansion analysis from Q3',
    relevanceScore: 0.87,
    usageContext: 'decision-rationale',
    retrievedAtUtc: '2026-01-15T09:55:00Z',
    ...overrides,
  };
}

// ── Tests ──

describe('PolicyEvaluationResult contract', () => {
  it('all rules passed — no violations', () => {
    const evaluation = makePolicyEvaluation({
      isAllowed: true,
      guardrailViolations: [],
      rulesEvaluated: [
        makePolicyRule({ ruleName: 'RequireApproval', passed: true }),
        makePolicyRule({ ruleName: 'BudgetLimit', passed: true }),
        makePolicyRule({ ruleName: 'RiskThreshold', passed: true }),
      ],
    });
    expect(evaluation.isAllowed).toBe(true);
    expect(evaluation.guardrailViolations).toHaveLength(0);
    expect(evaluation.rulesEvaluated.every(r => r.passed)).toBe(true);
  });

  it('guardrail violations present', () => {
    const evaluation = makePolicyEvaluation({
      isAllowed: false,
      riskScore: 85,
      guardrailViolations: ['Budget exceeded', 'Risk threshold breached'],
      rulesEvaluated: [
        makePolicyRule({ ruleName: 'BudgetLimit', passed: false, riskContribution: 40 }),
        makePolicyRule({ ruleName: 'RiskThreshold', passed: false, riskContribution: 45 }),
      ],
    });
    expect(evaluation.isAllowed).toBe(false);
    expect(evaluation.guardrailViolations).toHaveLength(2);
    expect(evaluation.rulesEvaluated.some(r => !r.passed)).toBe(true);
  });

  it('risk score between 0 and 100', () => {
    const evaluation = makePolicyEvaluation({ riskScore: 25 });
    expect(evaluation.riskScore).toBeGreaterThanOrEqual(0);
    expect(evaluation.riskScore).toBeLessThanOrEqual(100);

    const highRisk = makePolicyEvaluation({ riskScore: 95 });
    expect(highRisk.riskScore).toBeGreaterThanOrEqual(0);
    expect(highRisk.riskScore).toBeLessThanOrEqual(100);
  });
});

describe('MemoryContextReference contract', () => {
  it('relevance scores between 0 and 1', () => {
    const ref = makeMemoryContextRef({ relevanceScore: 0.87 });
    expect(ref.relevanceScore).toBeGreaterThanOrEqual(0);
    expect(ref.relevanceScore).toBeLessThanOrEqual(1);
  });

  it('handles edge relevance scores', () => {
    const low = makeMemoryContextRef({ relevanceScore: 0.0 });
    expect(low.relevanceScore).toBe(0);

    const high = makeMemoryContextRef({ relevanceScore: 1.0 });
    expect(high.relevanceScore).toBe(1);
  });
});

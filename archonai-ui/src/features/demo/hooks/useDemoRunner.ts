import { useState, useCallback, useRef } from 'react';
import type { DemoResult, DemoScenario, DemoRunnerState } from '../types';

const API_BASE = '/api/v1/demo/governance-loop';

const PHASE_ANIMATION_DELAY = 600; // ms between each phase lighting up

/* ── Mock data for each scenario ────────────────────────────────── */

const MOCK_FINANCE: DemoResult = {
  scenario: 'finance-approval',
  phases: [
    {
      name: 'ai-reasoning',
      output: JSON.stringify({
        response:
          'The proposed $500K enterprise software licensing expenditure presents moderate financial risk. Historical vendor performance data indicates 87% on-time delivery for contracts of this magnitude. However, the current fiscal quarter shows tightened discretionary spending limits, and this purchase would consume 34% of the remaining Q3 technology budget. Recommend approval contingent on staggered payment terms to preserve cash flow flexibility.',
        model: 'openai.gpt-4.1-mini',
        tokensUsed: 247,
      }),
      latencyMs: 1842,
    },
    {
      name: 'policy-evaluation',
      output: JSON.stringify({
        isAllowed: true,
        riskScore: 67,
        confidenceScore: 0.55,
        requiresApproval: true,
        approvalState: 'pending',
        guardrailViolations: [],
        reason:
          'Amount exceeds $100K threshold — requires human approval per financial-controls-v3 policy.',
      }),
      latencyMs: 124,
    },
    {
      name: 'approval-gate',
      output: JSON.stringify({
        approvalGateId: 'b7f3a1d2-9e4c-4f8b-a6d1-3c5e7f9a2b4d',
        status: 'auto-approved',
        hasOverrideToken: true,
      }),
      latencyMs: 89,
    },
    {
      name: 'gated-execution',
      output: JSON.stringify({
        executed: true,
        success: true,
        error: null,
      }),
      latencyMs: 312,
    },
    {
      name: 'outcome-recording',
      output: JSON.stringify({
        decisionId: 'c4e8f2a1-7b3d-4a9e-8f1c-2d6e4a8b5c3f',
        expectedOutcomeSummary:
          'Purchase order approved with staggered payment terms. Expected vendor delivery within 45 business days.',
        confidenceAtPrediction: 0.55,
      }),
      latencyMs: 67,
    },
  ],
  trustTier: 'medium-risk',
  approvalGateId: 'b7f3a1d2-9e4c-4f8b-a6d1-3c5e7f9a2b4d',
  approvalStatus: 'auto-approved',
  overrideToken: 'hmac-sha256:a4f8c2e1b7d39e5f6a1c8d4b2e7f3a9d',
  trustLineageUrl: '/api/v1/trust-lineage/c4e8f2a1-7b3d-4a9e-8f1c-2d6e4a8b5c3f',
  totalLatencyMs: 2434,
  environmentReport: {
    readinessTier: 'production',
    readinessSummary: 'All governance services operational. 2 cloud providers active.',
    cloudProvidersActive: 2,
    localProviderActive: false,
    defaultModel: 'openai.gpt-4.1-mini',
  },
};

const MOCK_SALES: DemoResult = {
  scenario: 'sales-anomaly',
  phases: [
    {
      name: 'ai-reasoning',
      output: JSON.stringify({
        response:
          'EMEA region shows a +35% revenue spike in Q3 compared to historical seasonal averages. Primary driver appears to be three large enterprise deals closed in the DACH sub-region. While positive, this anomaly warrants investigation — similar spikes in prior years correlated with channel-stuffing patterns that reversed in Q4. Recommend flagging for revenue recognition review and adjusting forecast models.',
        model: 'openai.gpt-4.1-mini',
        tokensUsed: 198,
      }),
      latencyMs: 1567,
    },
    {
      name: 'policy-evaluation',
      output: JSON.stringify({
        isAllowed: true,
        riskScore: 42,
        confidenceScore: 0.72,
        requiresApproval: false,
        approvalState: 'not-required',
        guardrailViolations: [],
        reason:
          'Anomaly analysis is read-only. Risk below action threshold — no approval required.',
      }),
      latencyMs: 98,
    },
    {
      name: 'approval-gate',
      output: JSON.stringify({
        approvalGateId: 'e2d4f6a8-1b3c-5e7d-9f2a-4c6e8b1d3f5a',
        status: 'not-required',
        hasOverrideToken: false,
      }),
      latencyMs: 12,
    },
    {
      name: 'gated-execution',
      output: JSON.stringify({
        executed: true,
        success: true,
        error: null,
      }),
      latencyMs: 245,
    },
    {
      name: 'outcome-recording',
      output: JSON.stringify({
        decisionId: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
        expectedOutcomeSummary:
          'Sales anomaly flagged for revenue recognition review. Forecast models queued for recalibration.',
        confidenceAtPrediction: 0.72,
      }),
      latencyMs: 54,
    },
  ],
  trustTier: 'low-risk',
  approvalGateId: null,
  approvalStatus: 'not-required',
  overrideToken: null,
  trustLineageUrl: '/api/v1/trust-lineage/a1b2c3d4-e5f6-7890-abcd-ef1234567890',
  totalLatencyMs: 1976,
  environmentReport: {
    readinessTier: 'production',
    readinessSummary: 'All governance services operational. 2 cloud providers active.',
    cloudProvidersActive: 2,
    localProviderActive: false,
    defaultModel: 'openai.gpt-4.1-mini',
  },
};

const MOCK_OPS: DemoResult = {
  scenario: 'ops-escalation',
  phases: [
    {
      name: 'ai-reasoning',
      output: JSON.stringify({
        response:
          'Production pipeline has recorded 3 consecutive deployment failures in the last 4 hours. Root cause analysis indicates a dependency conflict introduced in build #4,891. The failure pattern matches a known regression vector — container image layer caching is returning stale artifacts. Recommend immediate rollback to build #4,888 and purging the build cache. Escalation to on-call SRE is warranted given the 99.9% SLA breach window.',
        model: 'openai.gpt-4.1-mini',
        tokensUsed: 276,
      }),
      latencyMs: 2103,
    },
    {
      name: 'policy-evaluation',
      output: JSON.stringify({
        isAllowed: true,
        riskScore: 84,
        confidenceScore: 0.41,
        requiresApproval: true,
        approvalState: 'pending',
        guardrailViolations: [],
        reason:
          'Production rollback classified as high-risk action. Requires SRE approval per incident-response-v2 policy.',
      }),
      latencyMs: 156,
    },
    {
      name: 'approval-gate',
      output: JSON.stringify({
        approvalGateId: 'f9a8b7c6-d5e4-3f2a-1b0c-9d8e7f6a5b4c',
        status: 'auto-approved',
        hasOverrideToken: true,
      }),
      latencyMs: 102,
    },
    {
      name: 'gated-execution',
      output: JSON.stringify({
        executed: true,
        success: true,
        error: null,
      }),
      latencyMs: 487,
    },
    {
      name: 'outcome-recording',
      output: JSON.stringify({
        decisionId: 'd4c3b2a1-0f9e-8d7c-6b5a-4e3d2c1b0a9f',
        expectedOutcomeSummary:
          'Rollback to build #4,888 executed. Build cache purged. SRE team notified via PagerDuty.',
        confidenceAtPrediction: 0.41,
      }),
      latencyMs: 71,
    },
  ],
  trustTier: 'high-risk',
  approvalGateId: 'f9a8b7c6-d5e4-3f2a-1b0c-9d8e7f6a5b4c',
  approvalStatus: 'auto-approved',
  overrideToken: 'hmac-sha256:c7d8e9f0a1b2c3d4e5f6a7b8c9d0e1f2',
  trustLineageUrl: '/api/v1/trust-lineage/d4c3b2a1-0f9e-8d7c-6b5a-4e3d2c1b0a9f',
  totalLatencyMs: 2919,
  environmentReport: {
    readinessTier: 'production',
    readinessSummary: 'All governance services operational. 2 cloud providers active.',
    cloudProvidersActive: 2,
    localProviderActive: false,
    defaultModel: 'openai.gpt-4.1-mini',
  },
};

const MOCK_DATA: Record<DemoScenario, DemoResult> = {
  'finance-approval': MOCK_FINANCE,
  'sales-anomaly': MOCK_SALES,
  'ops-escalation': MOCK_OPS,
};

/* ── Hook ───────────────────────────────────────────────────────── */

export function useDemoRunner() {
  const [state, setState] = useState<DemoRunnerState>({
    result: null,
    isLoading: false,
    error: null,
    activePhaseIndex: -1,
    isAnimating: false,
    isMockData: false,
  });

  const animationTimers = useRef<ReturnType<typeof setTimeout>[]>([]);

  const clearAnimations = useCallback(() => {
    animationTimers.current.forEach(clearTimeout);
    animationTimers.current = [];
  }, []);

  const animatePhases = useCallback(
    (result: DemoResult) => {
      clearAnimations();
      setState((prev) => ({ ...prev, isAnimating: true, activePhaseIndex: -1 }));

      result.phases.forEach((_, index) => {
        const timer = setTimeout(() => {
          setState((prev) => ({ ...prev, activePhaseIndex: index }));
          if (index === result.phases.length - 1) {
            const finish = setTimeout(() => {
              setState((prev) => ({ ...prev, isAnimating: false }));
            }, PHASE_ANIMATION_DELAY);
            animationTimers.current.push(finish);
          }
        }, PHASE_ANIMATION_DELAY * (index + 1));
        animationTimers.current.push(timer);
      });
    },
    [clearAnimations],
  );

  const runScenario = useCallback(
    async (scenario: DemoScenario) => {
      clearAnimations();
      setState({
        result: null,
        isLoading: true,
        error: null,
        activePhaseIndex: -1,
        isAnimating: false,
        isMockData: false,
      });

      try {
        const response = await fetch(API_BASE, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            scenario,
            autoApprove: true,
          }),
        });

        if (!response.ok) {
          throw new Error(`API returned ${response.status}`);
        }

        const data: DemoResult = await response.json();
        setState((prev) => ({
          ...prev,
          result: data,
          isLoading: false,
          isMockData: false,
        }));
        animatePhases(data);
      } catch {
        // Fall back to mock data
        const mockResult = MOCK_DATA[scenario];
        setState((prev) => ({
          ...prev,
          result: mockResult,
          isLoading: false,
          error: null,
          isMockData: true,
        }));
        animatePhases(mockResult);
      }
    },
    [clearAnimations, animatePhases],
  );

  const reset = useCallback(() => {
    clearAnimations();
    setState({
      result: null,
      isLoading: false,
      error: null,
      activePhaseIndex: -1,
      isAnimating: false,
      isMockData: false,
    });
  }, [clearAnimations]);

  return { ...state, runScenario, reset };
}

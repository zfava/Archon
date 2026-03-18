import { useCallback, useState } from 'react';
import { api } from '../../../api/client';
import type {
  InspectionSummary,
  DecisionRationaleBundle,
  PolicyEvaluationResult,
  MemoryContextReference,
  WorkflowFailureDiagnostics,
} from '../types';

export interface InspectionState {
  loading: boolean;
  error: string | null;
  summaries: InspectionSummary[];
  rationaleBundle: DecisionRationaleBundle | null;
  policyEvaluation: PolicyEvaluationResult | null;
  memoryReferences: MemoryContextReference[];
  workflowDiagnostics: WorkflowFailureDiagnostics | null;
}

export function useInspection() {
  const [state, setState] = useState<InspectionState>({
    loading: false,
    error: null,
    summaries: [],
    rationaleBundle: null,
    policyEvaluation: null,
    memoryReferences: [],
    workflowDiagnostics: null,
  });

  const wrap = useCallback(
    async <T>(fn: () => Promise<T>, update: (s: InspectionState, r: T) => Partial<InspectionState>) => {
      setState((s) => ({ ...s, loading: true, error: null }));
      try {
        const result = await fn();
        setState((s) => ({ ...s, loading: false, ...update(s, result) }));
        return result;
      } catch (err: unknown) {
        const message = err instanceof Error ? err.message : String(err);
        setState((s) => ({ ...s, loading: false, error: message }));
        return null;
      }
    },
    [],
  );

  const loadSummaries = useCallback(
    (subjectType?: string, domain?: string, limit?: number) =>
      wrap(
        () => api.getInspectionSummaries(subjectType, domain, limit) as Promise<InspectionSummary[]>,
        (_s, r) => ({ summaries: r }),
      ),
    [wrap],
  );

  const inspectDecisionRationale = useCallback(
    (decisionId: string) =>
      wrap(
        () => api.inspectDecisionRationale(decisionId) as Promise<DecisionRationaleBundle>,
        (_s, r) => ({ rationaleBundle: r }),
      ),
    [wrap],
  );

  const inspectPolicyEvaluation = useCallback(
    (subjectType: string, subjectId: string) =>
      wrap(
        () => api.inspectPolicyEvaluation(subjectType, subjectId) as Promise<PolicyEvaluationResult>,
        (_s, r) => ({ policyEvaluation: r }),
      ),
    [wrap],
  );

  const inspectMemoryReferences = useCallback(
    (subjectType: string, subjectId: string) =>
      wrap(
        () => api.inspectMemoryReferences(subjectType, subjectId) as Promise<MemoryContextReference[]>,
        (_s, r) => ({ memoryReferences: r }),
      ),
    [wrap],
  );

  const inspectWorkflowDiagnostics = useCallback(
    (workflowId: string) =>
      wrap(
        () => api.inspectWorkflowDiagnostics(workflowId) as Promise<WorkflowFailureDiagnostics>,
        (_s, r) => ({ workflowDiagnostics: r }),
      ),
    [wrap],
  );

  return {
    ...state,
    loadSummaries,
    inspectDecisionRationale,
    inspectPolicyEvaluation,
    inspectMemoryReferences,
    inspectWorkflowDiagnostics,
  };
}

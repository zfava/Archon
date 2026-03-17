import { useCallback, useState } from 'react';
import { api } from '../../../api/client';
import type { DecisionExplanation, StrategyExplanation, AgentExplanation } from '../types';

export interface ExplanationsState {
  loading: boolean;
  error: string | null;
  strategyExplanation: StrategyExplanation | null;
  agentExplanation: AgentExplanation | null;
  decisionExplanation: DecisionExplanation | null;
}

export function useExplanations() {
  const [state, setState] = useState<ExplanationsState>({
    loading: false,
    error: null,
    strategyExplanation: null,
    agentExplanation: null,
    decisionExplanation: null,
  });

  const explainStrategy = useCallback(
    async (goalId: string, goalTitle: string, candidateStrategies: string[]) => {
      setState((s) => ({ ...s, loading: true, error: null }));
      try {
        const result = (await api.explainStrategy({
          goalId,
          goalTitle,
          candidateStrategies,
        })) as StrategyExplanation;
        setState((s) => ({ ...s, loading: false, strategyExplanation: result }));
        return result;
      } catch (err: unknown) {
        const message = err instanceof Error ? err.message : String(err);
        setState((s) => ({ ...s, loading: false, error: message }));
        return null;
      }
    },
    [],
  );

  const explainAgent = useCallback(
    async (requiredCapability: string, taskType?: string) => {
      setState((s) => ({ ...s, loading: true, error: null }));
      try {
        const result = (await api.explainAgent({
          requiredCapability,
          taskType: taskType || null,
        })) as AgentExplanation;
        setState((s) => ({ ...s, loading: false, agentExplanation: result }));
        return result;
      } catch (err: unknown) {
        const message = err instanceof Error ? err.message : String(err);
        setState((s) => ({ ...s, loading: false, error: message }));
        return null;
      }
    },
    [],
  );

  const explainDecision = useCallback(
    async (
      goalId: string,
      goalTitle: string,
      candidateStrategies: string[],
      requiredCapability: string,
      taskType?: string,
    ) => {
      setState((s) => ({ ...s, loading: true, error: null }));
      try {
        const result = (await api.explainDecision({
          goalId,
          goalTitle,
          candidateStrategies,
          requiredCapability,
          taskType: taskType || null,
        })) as DecisionExplanation;
        setState((s) => ({
          ...s,
          loading: false,
          decisionExplanation: result,
          strategyExplanation: result.strategyExplanation,
          agentExplanation: result.agentExplanation,
        }));
        return result;
      } catch (err: unknown) {
        const message = err instanceof Error ? err.message : String(err);
        setState((s) => ({ ...s, loading: false, error: message }));
        return null;
      }
    },
    [],
  );

  return {
    ...state,
    explainStrategy,
    explainAgent,
    explainDecision,
  };
}

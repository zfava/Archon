import { useCallback, useEffect, useState } from 'react';
import { api } from '../../../api/client';
import type {
  OperationalGoal,
  SimulationGuidedPlan,
  TaskGraph,
} from '../../command/types';

export interface StrategyState {
  loading: boolean;
  error: string | null;
  goal: OperationalGoal | null;
  plan: SimulationGuidedPlan | null;
  graph: TaskGraph | null;
}

export function useStrategy(goalId: string | undefined) {
  const [state, setState] = useState<StrategyState>({
    loading: false,
    error: null,
    goal: null,
    plan: null,
    graph: null,
  });

  const load = useCallback(async (id: string) => {
    setState((s) => ({ ...s, loading: true, error: null }));

    try {
      const goal = (await api.getGoal(id)) as OperationalGoal;
      const plan = (await api.getGuidedPlan(id)) as SimulationGuidedPlan;
      const graph = plan.selectedTaskGraph;

      setState({ loading: false, error: null, goal, plan, graph });
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : String(err);
      setState((s) => ({ ...s, loading: false, error: message }));
    }
  }, []);

  useEffect(() => {
    if (goalId) load(goalId);
  }, [goalId, load]);

  return state;
}

import { useCallback, useRef, useState } from 'react';
import { api } from '../../../api/client';
import { ApiError } from '../../../api/errors';
import type {
  CommandPhase,
  CommandResult,
  OperationalGoal,
  SimulationGuidedPlan,
  TaskGraphDispatchResult,
} from '../types';

function makeId() {
  return crypto.randomUUID?.() ?? Math.random().toString(36).slice(2);
}

/**
 * Parse natural-language command into an intent the planner understands.
 * This is a lightweight client-side parser — real NLU lives server-side.
 */
function parseIntent(input: string): {
  title: string;
  description: string;
  strategies?: string[];
} {
  const trimmed = input.trim();
  const strategyMatch = trimmed.match(/(?:using|with|via)\s+(?:strategy\s+)?["']?(\w[\w\s,]+\w)["']?\s*$/i);
  let strategies: string[] | undefined;
  let cleaned = trimmed;

  if (strategyMatch) {
    strategies = strategyMatch[1].split(/[,\s]+/).filter(Boolean);
    cleaned = trimmed.slice(0, strategyMatch.index).trim();
  }

  return { title: cleaned, description: cleaned, strategies };
}

/** Extract a user-friendly message from any error, with ApiError awareness. */
function errorMessage(err: unknown): string {
  if (err instanceof ApiError) {
    // For cancellation, don't surface as an error
    if (err.code === 'CANCELLED') return 'Request was cancelled.';
    return err.message;
  }
  return err instanceof Error ? err.message : String(err);
}

export function useCommand() {
  const [history, setHistory] = useState<CommandResult[]>([]);
  const [phase, setPhase] = useState<CommandPhase>('idle');
  const abortRef = useRef<AbortController | null>(null);

  const updateCurrent = useCallback(
    (patch: Partial<CommandResult>) =>
      setHistory((h) => {
        const copy = [...h];
        const last = copy[copy.length - 1];
        if (last) copy[copy.length - 1] = { ...last, ...patch };
        return copy;
      }),
    [],
  );

  /** Submit a natural-language command. */
  const submit = useCallback(
    async (input: string) => {
      abortRef.current?.abort();
      const ac = new AbortController();
      abortRef.current = ac;
      const signal = ac.signal;

      const id = makeId();
      const entry: CommandResult = {
        id,
        input,
        goal: null,
        plan: null,
        comparison: null,
        dispatch: null,
        phase: 'parsing',
        error: null,
        timestamp: new Date().toISOString(),
      };

      setHistory((h) => [...h, entry]);
      setPhase('parsing');

      try {
        // Phase 1 — parse intent
        const intent = parseIntent(input);

        // Phase 2 — generate goals
        setPhase('planning');
        updateCurrent({ phase: 'planning' });

        const genResult = await api.generateGoals(signal) as {
          generatedGoals: OperationalGoal[];
        };

        // Pick the best-matching goal or use the first one
        let goal = genResult.generatedGoals.find(
          (g) =>
            g.title.toLowerCase().includes(intent.title.toLowerCase().slice(0, 20)) ||
            g.description.toLowerCase().includes(intent.title.toLowerCase().slice(0, 20)),
        ) ?? genResult.generatedGoals[0] ?? null;

        if (!goal) {
          throw new ApiError('VALIDATION_ERROR', 'No goals generated from current business signals.');
        }

        // Approve goal
        await api.approveGoal(goal.goalId, signal);
        goal = { ...goal, status: 'Approved' };

        updateCurrent({ goal, phase: 'simulating' });
        setPhase('simulating');

        // Phase 3 — get simulation-guided plan
        const plan = (await api.getGuidedPlan(
          goal.goalId,
          intent.strategies,
          signal,
        )) as SimulationGuidedPlan;

        const comparison = plan.comparisonResult;

        updateCurrent({ plan, comparison, phase: 'ready' });
        setPhase('ready');
      } catch (err: unknown) {
        // Silently ignore if this command was cancelled/aborted
        if (signal.aborted) return;

        const message = errorMessage(err);
        updateCurrent({ phase: 'error', error: message });
        setPhase('error');
      }
    },
    [updateCurrent],
  );

  /** Execute the plan for the latest command. */
  const execute = useCallback(async () => {
    const last = history[history.length - 1];
    if (!last?.plan) return;

    abortRef.current?.abort();
    const ac = new AbortController();
    abortRef.current = ac;

    setPhase('executing');
    updateCurrent({ phase: 'executing' });

    try {
      const dispatch = (await api.dispatchGraph(
        last.plan.selectedTaskGraph.graphId,
        ac.signal,
      )) as TaskGraphDispatchResult;

      updateCurrent({ dispatch, phase: 'complete' });
      setPhase('complete');
    } catch (err: unknown) {
      if (ac.signal.aborted) return;

      const message = errorMessage(err);
      updateCurrent({ phase: 'error', error: message });
      setPhase('error');
    }
  }, [history, updateCurrent]);

  /** Simulate with different strategies. */
  const simulate = useCallback(
    async (strategies: string[]) => {
      const last = history[history.length - 1];
      if (!last?.goal) return;

      abortRef.current?.abort();
      const ac = new AbortController();
      abortRef.current = ac;

      setPhase('simulating');
      updateCurrent({ phase: 'simulating' });

      try {
        const plan = (await api.getGuidedPlan(
          last.goal.goalId,
          strategies,
          ac.signal,
        )) as SimulationGuidedPlan;

        updateCurrent({
          plan,
          comparison: plan.comparisonResult,
          phase: 'ready',
        });
        setPhase('ready');
      } catch (err: unknown) {
        if (ac.signal.aborted) return;

        const message = errorMessage(err);
        updateCurrent({ phase: 'error', error: message });
        setPhase('error');
      }
    },
    [history, updateCurrent],
  );

  /** Cancel the current command. */
  const cancel = useCallback(async () => {
    abortRef.current?.abort();
    const last = history[history.length - 1];
    if (last?.goal) {
      await api.cancelGoal(last.goal.goalId, 'User cancelled').catch(() => {});
    }
    updateCurrent({ phase: 'idle' });
    setPhase('idle');
  }, [history, updateCurrent]);

  const current = history[history.length - 1] ?? null;

  return { history, current, phase, submit, execute, simulate, cancel };
}

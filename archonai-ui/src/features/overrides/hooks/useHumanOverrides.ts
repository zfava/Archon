import { useCallback, useEffect, useState } from 'react';
import { api } from '../../../api/client';
import type { HumanOverrideEntry, OverrideLog, OverrideResult } from '../types';

export interface OverridesState {
  loading: boolean;
  acting: boolean;
  error: string | null;
  entries: HumanOverrideEntry[];
  lastResult: OverrideResult | null;
  workflowFilter: string;
}

export function useHumanOverrides() {
  const [state, setState] = useState<OverridesState>({
    loading: true,
    acting: false,
    error: null,
    entries: [],
    lastResult: null,
    workflowFilter: '',
  });

  const loadLog = useCallback(async (workflowId?: string) => {
    setState((s) => ({ ...s, loading: true, error: null }));
    try {
      const log = (await api.getOverrideLog(workflowId || undefined)) as OverrideLog;
      setState((s) => ({
        ...s,
        loading: false,
        entries: log.entries,
      }));
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : String(err);
      setState((s) => ({ ...s, loading: false, error: message }));
    }
  }, []);

  useEffect(() => {
    queueMicrotask(() => loadLog());
  }, [loadLog]);

  const pauseWorkflow = useCallback(
    async (workflowId: string, reason: string, performedBy: string) => {
      setState((s) => ({ ...s, acting: true, error: null, lastResult: null }));
      try {
        const result = (await api.pauseWorkflow({ workflowId, reason, performedBy })) as OverrideResult;
        setState((s) => ({ ...s, acting: false, lastResult: result }));
        await loadLog(state.workflowFilter || undefined);
        return result;
      } catch (err: unknown) {
        const message = err instanceof Error ? err.message : String(err);
        setState((s) => ({ ...s, acting: false, error: message }));
        return null;
      }
    },
    [loadLog, state.workflowFilter],
  );

  const resumeWorkflow = useCallback(
    async (workflowId: string, reason: string, performedBy: string) => {
      setState((s) => ({ ...s, acting: true, error: null, lastResult: null }));
      try {
        const result = (await api.resumeWorkflow({ workflowId, reason, performedBy })) as OverrideResult;
        setState((s) => ({ ...s, acting: false, lastResult: result }));
        await loadLog(state.workflowFilter || undefined);
        return result;
      } catch (err: unknown) {
        const message = err instanceof Error ? err.message : String(err);
        setState((s) => ({ ...s, acting: false, error: message }));
        return null;
      }
    },
    [loadLog, state.workflowFilter],
  );

  const cancelAction = useCallback(
    async (workflowId: string, reason: string, performedBy: string, taskId?: string) => {
      setState((s) => ({ ...s, acting: true, error: null, lastResult: null }));
      try {
        const result = (await api.cancelOverrideAction({
          workflowId,
          taskId: taskId || null,
          reason,
          performedBy,
        })) as OverrideResult;
        setState((s) => ({ ...s, acting: false, lastResult: result }));
        await loadLog(state.workflowFilter || undefined);
        return result;
      } catch (err: unknown) {
        const message = err instanceof Error ? err.message : String(err);
        setState((s) => ({ ...s, acting: false, error: message }));
        return null;
      }
    },
    [loadLog, state.workflowFilter],
  );

  const modifyStrategy = useCallback(
    async (
      workflowId: string,
      previousStrategy: string,
      newStrategy: string,
      reason: string,
      performedBy: string,
    ) => {
      setState((s) => ({ ...s, acting: true, error: null, lastResult: null }));
      try {
        const result = (await api.modifyStrategy({
          workflowId,
          previousStrategy,
          newStrategy,
          reason,
          performedBy,
        })) as OverrideResult;
        setState((s) => ({ ...s, acting: false, lastResult: result }));
        await loadLog(state.workflowFilter || undefined);
        return result;
      } catch (err: unknown) {
        const message = err instanceof Error ? err.message : String(err);
        setState((s) => ({ ...s, acting: false, error: message }));
        return null;
      }
    },
    [loadLog, state.workflowFilter],
  );

  const rollback = useCallback(
    async (workflowId: string, overrideId: string, reason: string, performedBy: string) => {
      setState((s) => ({ ...s, acting: true, error: null, lastResult: null }));
      try {
        const result = (await api.rollbackOverride({
          workflowId,
          overrideId,
          reason,
          performedBy,
        })) as OverrideResult;
        setState((s) => ({ ...s, acting: false, lastResult: result }));
        await loadLog(state.workflowFilter || undefined);
        return result;
      } catch (err: unknown) {
        const message = err instanceof Error ? err.message : String(err);
        setState((s) => ({ ...s, acting: false, error: message }));
        return null;
      }
    },
    [loadLog, state.workflowFilter],
  );

  const setWorkflowFilter = useCallback(
    (workflowId: string) => {
      setState((s) => ({ ...s, workflowFilter: workflowId }));
      loadLog(workflowId || undefined);
    },
    [loadLog],
  );

  return {
    ...state,
    pauseWorkflow,
    resumeWorkflow,
    cancelAction,
    modifyStrategy,
    rollback,
    setWorkflowFilter,
    reload: () => loadLog(state.workflowFilter || undefined),
  };
}

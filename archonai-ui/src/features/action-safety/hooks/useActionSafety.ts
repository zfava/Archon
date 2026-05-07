import { useState, useEffect, useCallback } from 'react';
import { api } from '../../../api/client';
import type {
  ActionSafetyClassification,
  GovernedActionRecord,
  RollbackSummary,
} from '../types';

export function useActionSafety(actionTypeFilter?: string) {
  const [classifications, setClassifications] = useState<ActionSafetyClassification[]>([]);
  const [actions, setActions] = useState<GovernedActionRecord[]>([]);
  const [summary, setSummary] = useState<RollbackSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [cls, acts, sum] = await Promise.all([
        api.getActionSafetyClassifications() as Promise<ActionSafetyClassification[]>,
        api.getGovernedActions() as Promise<GovernedActionRecord[]>,
        api.getActionSafetySummary() as Promise<RollbackSummary>,
      ]);
      setClassifications(actionTypeFilter ? cls.filter(c => c.actionType === actionTypeFilter) : cls);
      setActions(actionTypeFilter ? acts.filter(a => a.actionType === actionTypeFilter) : acts);
      setSummary(sum);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load action safety data');
    } finally {
      setLoading(false);
    }
  }, [actionTypeFilter]);

  useEffect(() => { load(); }, [load]);

  const triggerRollback = useCallback(async (actionId: string) => {
    const result = await api.triggerRollback(actionId) as GovernedActionRecord;
    setActions(prev => prev.map(a => a.id === actionId ? result : a));
    // Refresh summary
    const sum = await api.getActionSafetySummary() as RollbackSummary;
    setSummary(sum);
    return result;
  }, []);

  return { classifications, actions, summary, loading, error, refresh: load, triggerRollback };
}

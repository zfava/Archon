import { useState, useEffect, useCallback } from 'react';
import { api } from '../../../api/client';
import type { ProofDashboard, ProofTimeline } from '../types';

export function useProofDashboard() {
  const [dashboard, setDashboard] = useState<ProofDashboard | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await api.getProofDashboard() as ProofDashboard;
      setDashboard(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load proof dashboard');
      setDashboard(null);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  return { dashboard, loading, error, refresh: load };
}

export function useProofTimeline(decisionId: string | null) {
  const [timeline, setTimeline] = useState<ProofTimeline | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!decisionId) return;
    setLoading(true);
    setError(null);
    try {
      const data = await api.getProofTimeline(decisionId) as ProofTimeline;
      setTimeline(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load timeline');
      setTimeline(null);
    } finally {
      setLoading(false);
    }
  }, [decisionId]);

  useEffect(() => { load(); }, [load]);

  return { timeline, loading, error, refresh: load };
}

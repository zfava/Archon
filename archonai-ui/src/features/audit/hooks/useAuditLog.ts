import { useCallback, useEffect, useRef, useState } from 'react';
import { api } from '../../../api/client';
import type {
  AuditEntry,
  AuditFilters,
  AuditIntegrityResult,
  AuditLogStatus,
  AuditQueryResult,
} from '../types';

const PAGE_SIZE = 50;

interface AuditLogState {
  loading: boolean;
  entries: AuditEntry[];
  totalCount: number;
  hasMore: boolean;
  status: AuditLogStatus | null;
  integrity: AuditIntegrityResult | null;
  verifying: boolean;
  error: string | null;
  filters: AuditFilters;
  page: number;
}

const emptyFilters: AuditFilters = {
  category: '',
  subjectId: '',
  resourceType: '',
  search: '',
};

export function useAuditLog() {
  const [state, setState] = useState<AuditLogState>({
    loading: true,
    entries: [],
    totalCount: 0,
    hasMore: false,
    status: null,
    integrity: null,
    verifying: false,
    error: null,
    filters: emptyFilters,
    page: 0,
  });

  const mountedRef = useRef(true);
  useEffect(() => () => { mountedRef.current = false; }, []);

  const fetchEntries = useCallback(async (filters: AuditFilters, page: number) => {
    setState((s) => ({ ...s, loading: true, error: null }));
    try {
      const result = (await api.queryAuditEntries({
        category: filters.category || undefined,
        subjectId: filters.subjectId || undefined,
        resourceType: filters.resourceType || undefined,
        offset: page * PAGE_SIZE,
        limit: PAGE_SIZE,
      })) as AuditQueryResult;

      if (!mountedRef.current) return;

      // Client-side text search across description, eventType, action
      let filtered = result.entries;
      if (filters.search) {
        const q = filters.search.toLowerCase();
        filtered = filtered.filter(
          (e) =>
            e.description.toLowerCase().includes(q) ||
            e.eventType.toLowerCase().includes(q) ||
            e.action.toLowerCase().includes(q) ||
            e.subjectId.toLowerCase().includes(q) ||
            e.resourceId.toLowerCase().includes(q),
        );
      }

      setState((s) => ({
        ...s,
        loading: false,
        entries: filtered,
        totalCount: result.totalCount,
        hasMore: result.hasMore,
      }));
    } catch (err: unknown) {
      if (!mountedRef.current) return;
      const msg = err instanceof Error ? err.message : String(err);
      setState((s) => ({ ...s, loading: false, error: msg }));
    }
  }, []);

  const fetchStatus = useCallback(async () => {
    try {
      const status = (await api.getAuditStatus()) as AuditLogStatus;
      if (mountedRef.current) {
        setState((s) => ({ ...s, status }));
      }
    } catch {
      // non-critical
    }
  }, []);

  // Initial load
  useEffect(() => {
    fetchEntries(emptyFilters, 0);
    fetchStatus();
  }, [fetchEntries, fetchStatus]);

  const setFilters = useCallback(
    (filters: AuditFilters) => {
      setState((s) => ({ ...s, filters, page: 0 }));
      fetchEntries(filters, 0);
    },
    [fetchEntries],
  );

  const setPage = useCallback(
    (page: number) => {
      setState((s) => ({ ...s, page }));
      fetchEntries(state.filters, page);
    },
    [fetchEntries, state.filters],
  );

  const verifyIntegrity = useCallback(async () => {
    setState((s) => ({ ...s, verifying: true }));
    try {
      const result = (await api.verifyAuditIntegrity()) as AuditIntegrityResult;
      if (mountedRef.current) {
        setState((s) => ({ ...s, verifying: false, integrity: result }));
      }
    } catch (err: unknown) {
      if (!mountedRef.current) return;
      const msg = err instanceof Error ? err.message : String(err);
      setState((s) => ({ ...s, verifying: false, error: msg }));
    }
  }, []);

  const refresh = useCallback(() => {
    fetchEntries(state.filters, state.page);
    fetchStatus();
  }, [fetchEntries, fetchStatus, state.filters, state.page]);

  return {
    ...state,
    setFilters,
    setPage,
    verifyIntegrity,
    refresh,
    pageSize: PAGE_SIZE,
  };
}

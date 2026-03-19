import { useCallback, useState } from 'react';
import { api } from '../../../api/client';
import { ApiError } from '../../../api/errors';
import { useApiCall } from '../../../shared/useApiCall';
import type {
  AuditEntry,
  AuditFilters,
  AuditIntegrityResult,
  AuditLogStatus,
  AuditQueryResult,
} from '../types';

const PAGE_SIZE = 50;

const emptyFilters: AuditFilters = {
  category: '',
  subjectId: '',
  resourceType: '',
  search: '',
};

export function useAuditLog() {
  const [filters, setFiltersState] = useState<AuditFilters>(emptyFilters);
  const [page, setPageState] = useState(0);
  const [integrity, setIntegrity] = useState<AuditIntegrityResult | null>(null);
  const [verifying, setVerifying] = useState(false);

  // Main entries fetch — automatically re-fetches on filter/page changes, aborts stale requests
  const {
    data: queryResult,
    loading,
    error: queryError,
    refresh: refreshEntries,
  } = useApiCall<AuditQueryResult>(
    (signal) =>
      api.queryAuditEntries(
        {
          category: filters.category || undefined,
          subjectId: filters.subjectId || undefined,
          resourceType: filters.resourceType || undefined,
          offset: page * PAGE_SIZE,
          limit: PAGE_SIZE,
        },
        signal,
      ) as Promise<AuditQueryResult>,
    [filters.category, filters.subjectId, filters.resourceType, page],
  );

  // Status fetch — separate lifecycle
  const {
    data: status,
    refresh: refreshStatus,
  } = useApiCall<AuditLogStatus>(
    (signal) => api.getAuditStatus(signal) as Promise<AuditLogStatus>,
    [],
  );

  // Client-side text search across description, eventType, action
  let entries: AuditEntry[] = queryResult?.entries ?? [];
  if (filters.search && entries.length > 0) {
    const q = filters.search.toLowerCase();
    entries = entries.filter(
      (e) =>
        e.description.toLowerCase().includes(q) ||
        e.eventType.toLowerCase().includes(q) ||
        e.action.toLowerCase().includes(q) ||
        e.subjectId.toLowerCase().includes(q) ||
        e.resourceId.toLowerCase().includes(q),
    );
  }

  const setFilters = useCallback((f: AuditFilters) => {
    setFiltersState(f);
    setPageState(0);
  }, []);

  const setPage = useCallback((p: number) => {
    setPageState(p);
  }, []);

  const verifyIntegrity = useCallback(async () => {
    setVerifying(true);
    try {
      const result = (await api.verifyAuditIntegrity()) as AuditIntegrityResult;
      setIntegrity(result);
    } catch (err: unknown) {
      // Integrity verification errors — rethrow ApiError for upstream handling
      if (err instanceof ApiError) throw err;
    } finally {
      setVerifying(false);
    }
  }, []);

  const refresh = useCallback(() => {
    refreshEntries();
    refreshStatus();
  }, [refreshEntries, refreshStatus]);

  // Backward-compatible error: expose as string | null for existing consumers
  const error = queryError ? queryError.message : null;

  return {
    loading,
    entries,
    totalCount: queryResult?.totalCount ?? 0,
    hasMore: queryResult?.hasMore ?? false,
    status,
    integrity,
    verifying,
    error,
    filters,
    page,
    setFilters,
    setPage,
    verifyIntegrity,
    refresh,
    pageSize: PAGE_SIZE,
  };
}

import { useCallback, useState } from 'react';
import type { AuditFilters } from '../types';

interface Props {
  filters: AuditFilters;
  onChange: (filters: AuditFilters) => void;
}

const CATEGORIES = ['', 'agent', 'workflow', 'user', 'system', 'security'];
const RESOURCE_TYPES = ['', 'agent', 'workflow', 'task', 'policy', 'tenant', 'goal'];

export function AuditFiltersBar({ filters, onChange }: Props) {
  const [local, setLocal] = useState(filters);

  const apply = useCallback(
    (patch: Partial<AuditFilters>) => {
      const next = { ...local, ...patch };
      setLocal(next);
      onChange(next);
    },
    [local, onChange],
  );

  const clearAll = useCallback(() => {
    const cleared: AuditFilters = { category: '', subjectId: '', resourceType: '', search: '' };
    setLocal(cleared);
    onChange(cleared);
  }, [onChange]);

  const hasFilters = local.category || local.subjectId || local.resourceType || local.search;

  return (
    <div className="al-filters">
      {/* Search */}
      <div className="al-search-wrap">
        <svg className="al-search-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
          <circle cx="11" cy="11" r="8" />
          <path d="M21 21l-4.35-4.35" />
        </svg>
        <input
          type="text"
          className="al-search-input"
          placeholder="Search entries..."
          value={local.search}
          onChange={(e) => apply({ search: e.target.value })}
        />
      </div>

      {/* Category */}
      <select
        className="al-filter-select"
        value={local.category}
        onChange={(e) => apply({ category: e.target.value })}
      >
        <option value="">All Categories</option>
        {CATEGORIES.filter(Boolean).map((c) => (
          <option key={c} value={c}>{c.charAt(0).toUpperCase() + c.slice(1)}</option>
        ))}
      </select>

      {/* Resource type */}
      <select
        className="al-filter-select"
        value={local.resourceType}
        onChange={(e) => apply({ resourceType: e.target.value })}
      >
        <option value="">All Resources</option>
        {RESOURCE_TYPES.filter(Boolean).map((r) => (
          <option key={r} value={r}>{r.charAt(0).toUpperCase() + r.slice(1)}</option>
        ))}
      </select>

      {/* Subject ID */}
      <input
        type="text"
        className="al-filter-input"
        placeholder="Subject ID..."
        value={local.subjectId}
        onChange={(e) => apply({ subjectId: e.target.value })}
      />

      {hasFilters && (
        <button className="al-clear-btn" onClick={clearAll}>
          Clear
        </button>
      )}
    </div>
  );
}

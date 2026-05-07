import { Link } from 'react-router-dom';
import { useAuditLog } from './hooks/useAuditLog';
import { AuditStatusBar } from './components/AuditStatusBar';
import { AuditFiltersBar } from './components/AuditFiltersBar';
import { AuditEntryRow } from './components/AuditEntryRow';
import './audit.css';

export function AuditLogView() {
  const {
    loading,
    entries,
    totalCount,
    hasMore,
    status,
    integrity,
    verifying,
    error,
    filters,
    page,
    pageSize,
    setFilters,
    setPage,
    verifyIntegrity,
    refresh,
  } = useAuditLog();

  const startIdx = page * pageSize + 1;
  const endIdx = Math.min(startIdx + entries.length - 1, totalCount);

  return (
    <div className="al-view">
      {/* Header */}
      <header className="al-header">
        <div className="al-header-left">
          <Link to="/" className="al-back">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M19 12H5M12 19l-7-7 7-7" />
            </svg>
          </Link>
          <div>
            <h1 className="al-title">Audit Log</h1>
            <p className="al-subtitle">Immutable record of all system actions</p>
          </div>
        </div>
        <div className="al-header-right">
          <button className="al-refresh-btn" onClick={refresh} disabled={loading}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M23 4v6h-6M1 20v-6h6" />
              <path d="M3.51 9a9 9 0 0114.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0020.49 15" />
            </svg>
            Refresh
          </button>
        </div>
      </header>

      {/* Status bar */}
      <AuditStatusBar
        status={status}
        integrity={integrity}
        verifying={verifying}
        onVerify={verifyIntegrity}
      />

      {/* Filters */}
      <AuditFiltersBar filters={filters} onChange={setFilters} />

      {/* Error */}
      {error && (
        <div className="al-error">
          <span>{error}</span>
        </div>
      )}

      {/* Entry list */}
      <div className="al-entries">
        {loading && entries.length === 0 ? (
          <div className="al-loading">
            <div className="al-loading-bar" />
            <div className="al-loading-bar" />
            <div className="al-loading-bar" />
          </div>
        ) : entries.length === 0 ? (
          <div className="al-empty">
            <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="#27272a" strokeWidth="1.5">
              <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
              <polyline points="14 2 14 8 20 8" />
              <line x1="16" y1="13" x2="8" y2="13" />
              <line x1="16" y1="17" x2="8" y2="17" />
              <polyline points="10 9 9 9 8 9" />
            </svg>
            <p>No audit entries match your filters</p>
          </div>
        ) : (
          entries.map((entry) => <AuditEntryRow key={entry.id} entry={entry} />)
        )}
      </div>

      {/* Pagination */}
      {totalCount > 0 && (
        <div className="al-pagination">
          <span className="al-page-info">
            {startIdx}–{endIdx} of {totalCount}
          </span>
          <div className="al-page-btns">
            <button
              className="al-page-btn"
              disabled={page === 0}
              onClick={() => setPage(page - 1)}
            >
              Previous
            </button>
            <button
              className="al-page-btn"
              disabled={!hasMore}
              onClick={() => setPage(page + 1)}
            >
              Next
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

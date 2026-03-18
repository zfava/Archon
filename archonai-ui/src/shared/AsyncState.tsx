import { type ReactNode } from 'react';

/* ── Loading spinner ──────────────────────────────────────── */

export function LoadingState({ message = 'Loading...' }: { message?: string }) {
  return (
    <div className="async-state async-state--loading">
      <div className="phase-spinner" />
      <span>{message}</span>
    </div>
  );
}

/* ── Error banner ─────────────────────────────────────────── */

interface ErrorStateProps {
  message: string;
  onRetry?: () => void;
}

export function ErrorState({ message, onRetry }: ErrorStateProps) {
  return (
    <div className="async-state async-state--error">
      <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="var(--error)" strokeWidth="2">
        <circle cx="12" cy="12" r="10" />
        <line x1="15" y1="9" x2="9" y2="15" />
        <line x1="9" y1="9" x2="15" y2="15" />
      </svg>
      <span>{message}</span>
      {onRetry && (
        <button className="exec-btn exec-btn--secondary" onClick={onRetry} style={{ marginLeft: 'var(--space-2)' }}>
          Retry
        </button>
      )}
    </div>
  );
}

/* ── Empty state ──────────────────────────────────────────── */

interface EmptyStateProps {
  title: string;
  description?: string;
  icon?: ReactNode;
}

export function EmptyState({ title, description, icon }: EmptyStateProps) {
  return (
    <div className="async-state async-state--empty">
      {icon ?? (
        <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="var(--text-faint)" strokeWidth="1.5">
          <rect x="3" y="3" width="18" height="18" rx="2" />
          <line x1="9" y1="9" x2="15" y2="15" />
          <line x1="15" y1="9" x2="9" y2="15" />
        </svg>
      )}
      <h3 style={{ margin: 0, color: 'var(--text-secondary)' }}>{title}</h3>
      {description && <p style={{ margin: 0, color: 'var(--text-faint)', fontSize: 'var(--font-size-sm)' }}>{description}</p>}
    </div>
  );
}

/* ── Composed async boundary ──────────────────────────────── */

interface AsyncBoundaryProps {
  loading: boolean;
  error: string | null;
  empty?: boolean;
  emptyTitle?: string;
  emptyDescription?: string;
  loadingMessage?: string;
  onRetry?: () => void;
  children: ReactNode;
}

/**
 * Renders loading, error, or empty states before showing children.
 * Eliminates repeated loading/error/empty boilerplate in feature pages.
 */
export function AsyncBoundary({
  loading,
  error,
  empty = false,
  emptyTitle = 'No data',
  emptyDescription,
  loadingMessage,
  onRetry,
  children,
}: AsyncBoundaryProps) {
  if (loading) return <LoadingState message={loadingMessage} />;
  if (error) return <ErrorState message={error} onRetry={onRetry} />;
  if (empty) return <EmptyState title={emptyTitle} description={emptyDescription} />;
  return <>{children}</>;
}

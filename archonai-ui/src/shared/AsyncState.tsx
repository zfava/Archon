import { type ReactNode } from 'react';
import { ApiError } from '../api/errors';
import type { ApiErrorCode } from '../api/errors';

/* ── Loading spinner ──────────────────────────────────────── */

export function LoadingState({ message = 'Loading...' }: { message?: string }) {
  return (
    <div className="async-state async-state--loading">
      <div className="phase-spinner" />
      <span>{message}</span>
    </div>
  );
}

/* ── Per-code error messages ──────────────────────────────── */

const ERROR_MESSAGES: Record<ApiErrorCode, string> = {
  UNAUTHORIZED: 'Your session has expired. Please log in again.',
  FORBIDDEN: "You don't have permission to view this.",
  NOT_FOUND: 'The requested resource could not be found.',
  VALIDATION_ERROR: 'The request contained invalid data.',
  SERVER_ERROR: 'Something went wrong on our end. Our team has been notified.',
  NETWORK_ERROR: 'Unable to reach the server. Check your connection.',
  TIMEOUT: 'The request took too long. Please try again.',
  CANCELLED: 'The request was cancelled.',
};

/* ── Error banner ─────────────────────────────────────────── */

interface ErrorStateProps {
  message: string;
  /** Pass an ApiError for richer, code-specific display. */
  error?: ApiError | null;
  onRetry?: () => void;
}

export function ErrorState({ message, error, onRetry }: ErrorStateProps) {
  const code = error instanceof ApiError ? error.code : null;
  const displayMessage = code ? ERROR_MESSAGES[code] : message;

  return (
    <div className="async-state async-state--error">
      <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="var(--error)" strokeWidth="2">
        <circle cx="12" cy="12" r="10" />
        <line x1="15" y1="9" x2="9" y2="15" />
        <line x1="9" y1="9" x2="15" y2="15" />
      </svg>
      <span>{displayMessage}</span>

      {/* UNAUTHORIZED — redirect to login */}
      {code === 'UNAUTHORIZED' && (
        <a
          href="/login"
          className="exec-btn exec-btn--primary"
          style={{ marginLeft: 'var(--space-2)', textDecoration: 'none' }}
        >
          Log in
        </a>
      )}

      {/* FORBIDDEN — contact admin */}
      {code === 'FORBIDDEN' && (
        <span style={{ marginLeft: 'var(--space-2)', fontSize: 'var(--font-size-sm)', color: 'var(--text-muted)' }}>
          Contact your administrator for access.
        </span>
      )}

      {/* SERVER_ERROR / NETWORK_ERROR / TIMEOUT — retry */}
      {(code === 'SERVER_ERROR' || code === 'NETWORK_ERROR' || code === 'TIMEOUT' || !code) && onRetry && (
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
  /** String error message (backward-compatible) or ApiError for richer display. */
  error: string | ApiError | null;
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
  if (error) {
    const apiError = error instanceof ApiError ? error : null;
    const message = typeof error === 'string' ? error : error.message;
    return <ErrorState message={message} error={apiError} onRetry={onRetry} />;
  }
  if (empty) return <EmptyState title={emptyTitle} description={emptyDescription} />;
  return <>{children}</>;
}

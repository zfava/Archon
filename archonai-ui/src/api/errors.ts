/* ═══════════════════════════════════════════════════════
   Normalized API Error Types
   ═══════════════════════════════════════════════════════ */

export type ApiErrorCode =
  | 'UNAUTHORIZED'
  | 'FORBIDDEN'
  | 'NOT_FOUND'
  | 'VALIDATION_ERROR'
  | 'SERVER_ERROR'
  | 'NETWORK_ERROR'
  | 'TIMEOUT'
  | 'CANCELLED';

/**
 * Structured API error with a machine-readable code, HTTP status,
 * and optional detail payload for diagnostics.
 */
export class ApiError extends Error {
  readonly code: ApiErrorCode;
  readonly statusCode: number | undefined;
  readonly details: unknown;

  constructor(
    code: ApiErrorCode,
    message: string,
    statusCode?: number,
    details?: unknown,
  ) {
    super(message);
    this.name = 'ApiError';
    this.code = code;
    this.statusCode = statusCode;
    this.details = details;
  }
}

/**
 * Map an HTTP Response to a structured ApiError.
 * Call after confirming `!response.ok`.
 */
export function mapHttpError(response: Response, body?: string): ApiError {
  const status = response.status;
  const detail = body || response.statusText;

  if (status === 401) {
    return new ApiError('UNAUTHORIZED', 'Authentication required', status, detail);
  }
  if (status === 403) {
    return new ApiError('FORBIDDEN', 'You do not have permission to perform this action', status, detail);
  }
  if (status === 404) {
    return new ApiError('NOT_FOUND', 'The requested resource was not found', status, detail);
  }
  if (status === 400 || status === 422) {
    return new ApiError('VALIDATION_ERROR', detail || 'Validation failed', status, detail);
  }
  if (status >= 500) {
    return new ApiError('SERVER_ERROR', 'An internal server error occurred', status, detail);
  }
  return new ApiError('SERVER_ERROR', `HTTP ${status}: ${detail}`, status, detail);
}

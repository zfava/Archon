import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError } from '../api/errors';

interface UseApiCallResult<T> {
  data: T | null;
  loading: boolean;
  error: ApiError | null;
  refresh: () => void;
}

/**
 * Generic data-fetching hook with automatic AbortController management.
 *
 * - Creates a fresh AbortController on each fetch (mount + deps change + refresh).
 * - Aborts in-flight requests when deps change, on refresh, or on unmount.
 * - Prevents setState after unmount via signal check.
 *
 * @param apiFn  Async function that receives an AbortSignal and returns data.
 * @param deps   Dependency array — a new fetch is triggered whenever these change.
 */
export function useApiCall<T>(
  apiFn: (signal: AbortSignal) => Promise<T>,
  deps: unknown[],
): UseApiCallResult<T> {
  const [state, setState] = useState<{
    data: T | null;
    loading: boolean;
    error: ApiError | null;
  }>({ data: null, loading: true, error: null });

  // Keep the latest apiFn in a ref so the memoized `run` always calls the current version
  const fnRef = useRef(apiFn);
  fnRef.current = apiFn;

  const controllerRef = useRef<AbortController | null>(null);

  const run = useCallback(() => {
    // Abort any in-flight request
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;

    setState((s) => ({ ...s, loading: true, error: null }));

    fnRef.current(controller.signal)
      .then((data) => {
        if (!controller.signal.aborted) {
          setState({ data, loading: false, error: null });
        }
      })
      .catch((err: unknown) => {
        // Silently ignore aborted requests (unmount / deps change / refresh)
        if (controller.signal.aborted) return;

        const apiError =
          err instanceof ApiError
            ? err
            : new ApiError(
                'NETWORK_ERROR',
                err instanceof Error ? err.message : String(err),
              );
        setState((s) => ({ ...s, loading: false, error: apiError }));
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);

  useEffect(() => {
    run();
    return () => {
      controllerRef.current?.abort();
    };
  }, [run]);

  return { ...state, refresh: run };
}

import { useCallback, useEffect, useRef } from 'react';

/**
 * Lightweight frontend telemetry hook.
 * Collects page view timing, user actions, and error events.
 * Ships events to the backend via beacon API (non-blocking).
 */

interface TelemetryEvent {
  type: 'page_view' | 'action' | 'error' | 'perf';
  name: string;
  durationMs?: number;
  metadata?: Record<string, string | number | boolean>;
  timestamp: string;
}

const TELEMETRY_ENDPOINT = `${import.meta.env.VITE_API_BASE_URL ?? '/api/v1'}/telemetry/events`;
const BATCH_SIZE = 10;
const FLUSH_INTERVAL_MS = 30_000;

const buffer: TelemetryEvent[] = [];
let flushTimer: ReturnType<typeof setTimeout> | null = null;

function flush() {
  if (buffer.length === 0) return;
  const batch = buffer.splice(0, buffer.length);
  try {
    const body = JSON.stringify({ events: batch });
    if (typeof navigator.sendBeacon === 'function') {
      navigator.sendBeacon(TELEMETRY_ENDPOINT, body);
    }
  } catch {
    // Telemetry is best-effort
  }
}

function enqueue(event: TelemetryEvent) {
  buffer.push(event);
  if (buffer.length >= BATCH_SIZE) {
    flush();
  } else if (!flushTimer) {
    flushTimer = setTimeout(() => {
      flush();
      flushTimer = null;
    }, FLUSH_INTERVAL_MS);
  }
}

export function useTelemetry(pageName?: string) {
  const pageLoadRef = useRef(0);
  useEffect(() => { pageLoadRef.current = performance.now(); }, []);

  const trackPageView = useCallback((name?: string) => {
    enqueue({
      type: 'page_view',
      name: name ?? pageName ?? 'unknown',
      durationMs: Math.round(performance.now() - pageLoadRef.current),
      timestamp: new Date().toISOString(),
    });
  }, [pageName]);

  const trackAction = useCallback((action: string, metadata?: Record<string, string | number | boolean>) => {
    enqueue({
      type: 'action',
      name: action,
      metadata,
      timestamp: new Date().toISOString(),
    });
  }, []);

  const trackError = useCallback((error: string, metadata?: Record<string, string | number | boolean>) => {
    enqueue({
      type: 'error',
      name: error,
      metadata,
      timestamp: new Date().toISOString(),
    });
  }, []);

  const trackPerf = useCallback((label: string, durationMs: number, metadata?: Record<string, string | number | boolean>) => {
    enqueue({
      type: 'perf',
      name: label,
      durationMs,
      metadata,
      timestamp: new Date().toISOString(),
    });
  }, []);

  return { trackPageView, trackAction, trackError, trackPerf };
}

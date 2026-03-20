import { useCallback, useEffect, useRef, useState } from 'react';
import {
  HubConnectionBuilder,
  HubConnection,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';

const HUB_URL = `${import.meta.env.VITE_API_BASE_URL ?? ''}/hubs/inspection`;

export interface InspectionEvent {
  type: 'PolicyEvaluationRecorded' | 'MemoryReferenceRecorded' | 'WorkflowDiagnosticsUpdated';
  payload: Record<string, unknown>;
  receivedAt: string;
}

export type InspectionHubStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

export interface InspectionHubState {
  status: InspectionHubStatus;
  events: InspectionEvent[];
}

export function useInspectionHub() {
  const [state, setState] = useState<InspectionHubState>({
    status: 'disconnected',
    events: [],
  });

  const connRef = useRef<HubConnection | null>(null);
  const subjectRef = useRef<{ subjectType: string; subjectId: string } | null>(null);

  const addEvent = useCallback((type: InspectionEvent['type'], payload: Record<string, unknown>) => {
    const event: InspectionEvent = { type, payload, receivedAt: new Date().toISOString() };
    setState((s) => ({
      ...s,
      events: [event, ...s.events].slice(0, 100), // Keep last 100 events
    }));
  }, []);

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    connRef.current = connection;

    connection.on('PolicyEvaluationRecorded', (data: Record<string, unknown>) => {
      addEvent('PolicyEvaluationRecorded', data);
    });
    connection.on('MemoryReferenceRecorded', (data: Record<string, unknown>) => {
      addEvent('MemoryReferenceRecorded', data);
    });
    connection.on('WorkflowDiagnosticsUpdated', (data: Record<string, unknown>) => {
      addEvent('WorkflowDiagnosticsUpdated', data);
    });

    connection.onreconnecting(() => {
      setState((s) => ({ ...s, status: 'reconnecting' }));
    });

    connection.onreconnected(() => {
      setState((s) => ({ ...s, status: 'connected' }));
      // Re-subscribe after reconnect
      const sub = subjectRef.current;
      if (sub) {
        connection.invoke('SubscribeToSubject', sub.subjectType, sub.subjectId).catch(() => {});
      }
    });

    connection.onclose(() => {
      setState((s) => ({ ...s, status: 'disconnected' }));
    });

    const handleAuthExpired = () => {
      if (connection.state !== HubConnectionState.Disconnected) {
        connection.stop();
      }
      setState((s) => ({ ...s, status: 'disconnected' }));
    };
    window.addEventListener('auth:expired', handleAuthExpired);

    queueMicrotask(() => setState((s) => ({ ...s, status: 'connecting' })));

    connection
      .start()
      .then(() => {
        setState((s) => ({ ...s, status: 'connected' }));
      })
      .catch(() => {
        setState((s) => ({ ...s, status: 'disconnected' }));
      });

    return () => {
      window.removeEventListener('auth:expired', handleAuthExpired);
      if (connection.state !== HubConnectionState.Disconnected) {
        connection.stop();
      }
    };
  }, [addEvent]);

  const subscribe = useCallback((subjectType: string, subjectId: string) => {
    const conn = connRef.current;
    const prev = subjectRef.current;

    // Unsubscribe from previous subject
    if (prev && conn?.state === HubConnectionState.Connected) {
      conn.invoke('UnsubscribeFromSubject', prev.subjectType, prev.subjectId).catch(() => {});
    }

    subjectRef.current = { subjectType, subjectId };
    setState((s) => ({ ...s, events: [] }));

    if (conn?.state === HubConnectionState.Connected) {
      conn.invoke('SubscribeToSubject', subjectType, subjectId).catch(() => {});
    }
  }, []);

  const unsubscribe = useCallback(() => {
    const conn = connRef.current;
    const sub = subjectRef.current;

    if (sub && conn?.state === HubConnectionState.Connected) {
      conn.invoke('UnsubscribeFromSubject', sub.subjectType, sub.subjectId).catch(() => {});
    }
    subjectRef.current = null;
  }, []);

  return { ...state, subscribe, unsubscribe };
}

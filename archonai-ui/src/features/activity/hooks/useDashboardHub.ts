import { useCallback, useEffect, useRef, useState } from 'react';
import {
  HubConnectionBuilder,
  HubConnection,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import type {
  AgentActivityDashboard,
  TaskPerformanceDashboard,
  SystemHealthDashboard,
  ConnectionStatus,
  DashboardUpdate,
  SystemAlert,
} from '../types';

const HUB_URL = `${import.meta.env.VITE_API_BASE_URL ?? ''}/hubs/control-plane-dashboard`;

export interface DashboardState {
  connectionStatus: ConnectionStatus;
  agentActivity: AgentActivityDashboard | null;
  taskPerformance: TaskPerformanceDashboard | null;
  systemHealth: SystemHealthDashboard | null;
  alerts: SystemAlert[];
  lastUpdated: string | null;
}

export function useDashboardHub() {
  const [state, setState] = useState<DashboardState>({
    connectionStatus: 'disconnected',
    agentActivity: null,
    taskPerformance: null,
    systemHealth: null,
    alerts: [],
    lastUpdated: null,
  });

  const connRef = useRef<HubConnection | null>(null);

  const handleModuleUpdate = useCallback((update: DashboardUpdate) => {
    const now = new Date().toISOString();

    switch (update.module) {
      case 'agent-activity':
        setState((s) => ({
          ...s,
          agentActivity: update.payload as AgentActivityDashboard,
          lastUpdated: now,
        }));
        break;
      case 'task-performance':
        setState((s) => ({
          ...s,
          taskPerformance: update.payload as TaskPerformanceDashboard,
          lastUpdated: now,
        }));
        break;
      case 'system-health':
        setState((s) => ({
          ...s,
          systemHealth: update.payload as SystemHealthDashboard,
          lastUpdated: now,
        }));
        break;
    }
  }, []);

  const handleAlertsUpdate = useCallback((alerts: SystemAlert[]) => {
    setState((s) => ({ ...s, alerts, lastUpdated: new Date().toISOString() }));
  }, []);

  const handleFullDashboard = useCallback(
    (dashboard: {
      agentActivity: AgentActivityDashboard;
      taskPerformance: TaskPerformanceDashboard;
      systemHealth: SystemHealthDashboard;
    }) => {
      setState((s) => ({
        ...s,
        agentActivity: dashboard.agentActivity,
        taskPerformance: dashboard.taskPerformance,
        systemHealth: dashboard.systemHealth,
        alerts: dashboard.systemHealth?.activeAlerts ?? s.alerts,
        lastUpdated: new Date().toISOString(),
      }));
    },
    [],
  );

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    connRef.current = connection;

    connection.on('ModuleDashboardUpdate', handleModuleUpdate);
    connection.on('AlertsUpdate', handleAlertsUpdate);
    connection.on('FullDashboardUpdate', handleFullDashboard);

    connection.onreconnecting(() => {
      setState((s) => ({ ...s, connectionStatus: 'reconnecting' }));
    });

    connection.onreconnected(() => {
      setState((s) => ({ ...s, connectionStatus: 'connected' }));
      // Re-subscribe after reconnect
      connection.invoke('SubscribeToModule', 'agent-activity').catch(() => {});
      connection.invoke('SubscribeToModule', 'task-performance').catch(() => {});
      connection.invoke('SubscribeToModule', 'system-health').catch(() => {});
      connection.invoke('SubscribeToModule', 'alerts').catch(() => {});
      connection.invoke('RequestFullDashboard').catch(() => {});
    });

    connection.onclose(() => {
      setState((s) => ({ ...s, connectionStatus: 'disconnected' }));
    });

    setState((s) => ({ ...s, connectionStatus: 'connecting' }));

    connection
      .start()
      .then(async () => {
        setState((s) => ({ ...s, connectionStatus: 'connected' }));
        // Subscribe to all relevant modules
        await connection.invoke('SubscribeToModule', 'agent-activity');
        await connection.invoke('SubscribeToModule', 'task-performance');
        await connection.invoke('SubscribeToModule', 'system-health');
        await connection.invoke('SubscribeToModule', 'alerts');
        // Request initial snapshot
        await connection.invoke('RequestFullDashboard');
      })
      .catch(() => {
        setState((s) => ({ ...s, connectionStatus: 'disconnected' }));
      });

    return () => {
      if (connection.state !== HubConnectionState.Disconnected) {
        connection.stop();
      }
    };
  }, [handleModuleUpdate, handleAlertsUpdate, handleFullDashboard]);

  const refresh = useCallback(() => {
    const conn = connRef.current;
    if (conn?.state === HubConnectionState.Connected) {
      conn.invoke('RequestFullDashboard').catch(() => {});
    }
  }, []);

  return { ...state, refresh };
}

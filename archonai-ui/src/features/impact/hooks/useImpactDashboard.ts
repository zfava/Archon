import { useCallback, useEffect, useRef, useState } from 'react';
import {
  HubConnectionBuilder,
  HubConnection,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { api } from '../../../api/client';
import type {
  TaskPerformanceDashboard,
  AgentActivityDashboard,
  ModelUsageDashboard,
  SystemHealthDashboard,
  ImpactMetric,
  BeforeAfterMetric,
  ConnectionStatus,
  DashboardUpdate,
} from '../types';

const HUB_URL = `${import.meta.env.VITE_API_BASE_URL ?? ''}/hubs/control-plane-dashboard`;

// Baseline estimates — what operations look like without ArchonAI
const BASELINE = {
  taskCompletionRate: 0.62,
  avgExecutionTimeMs: 45000,
  avgCostPerTask: 12.5,
  agentSuccessRate: 0.7,
  modelCostPerRequest: 0.08,
  manualProcessingHoursPerTask: 2.5,
};

interface ImpactState {
  loading: boolean;
  error: string | null;
  connectionStatus: ConnectionStatus;
  taskPerformance: TaskPerformanceDashboard | null;
  agentActivity: AgentActivityDashboard | null;
  modelUsage: ModelUsageDashboard | null;
  systemHealth: SystemHealthDashboard | null;
  impactMetrics: ImpactMetric[];
  beforeAfter: BeforeAfterMetric[];
  lastUpdated: string | null;
}

function computeImpactMetrics(
  tasks: TaskPerformanceDashboard | null,
  agents: AgentActivityDashboard | null,
  models: ModelUsageDashboard | null,
): ImpactMetric[] {
  const metrics: ImpactMetric[] = [];

  if (tasks) {
    // Cost savings from automation efficiency
    const baselineCost = tasks.totalTasks * BASELINE.avgCostPerTask;
    const actualCost = tasks.totalTasks * tasks.averageCost;
    const costSaved = Math.max(0, baselineCost - actualCost);

    metrics.push({
      label: 'Cost Savings',
      value: costSaved,
      unit: '$',
      trend: tasks.averageCost > 0
        ? ((BASELINE.avgCostPerTask - tasks.averageCost) / BASELINE.avgCostPerTask) * 100
        : 0,
      trendLabel: 'vs manual baseline',
      category: 'cost',
    });

    // Time saved from automation (hours recovered)
    const hoursSaved = tasks.completedTasks * (BASELINE.manualProcessingHoursPerTask - (tasks.averageExecutionTimeMs / 3600000));

    metrics.push({
      label: 'Hours Recovered',
      value: Math.max(0, hoursSaved),
      unit: 'hrs',
      trend: tasks.averageExecutionTimeMs > 0
        ? ((BASELINE.avgExecutionTimeMs - tasks.averageExecutionTimeMs) / BASELINE.avgExecutionTimeMs) * 100
        : 0,
      trendLabel: 'faster execution',
      category: 'efficiency',
    });

    // Revenue impact — completion rate improvement drives revenue potential
    const completionLift = Math.max(0, tasks.overallCompletionRate - BASELINE.taskCompletionRate);
    const revenueImpact = completionLift * tasks.completedTasks * BASELINE.avgCostPerTask * 3;

    metrics.push({
      label: 'Revenue Impact',
      value: revenueImpact,
      unit: '$',
      trend: completionLift * 100,
      trendLabel: 'completion lift',
      category: 'revenue',
    });
  }

  if (agents) {
    // Agent efficiency gain
    const successLift = Math.max(0, agents.overallSuccessRate - BASELINE.agentSuccessRate);

    metrics.push({
      label: 'Agent Efficiency',
      value: agents.overallSuccessRate * 100,
      unit: '%',
      trend: successLift * 100,
      trendLabel: 'vs baseline',
      category: 'efficiency',
    });

    // Throughput — tasks automated
    metrics.push({
      label: 'Tasks Automated',
      value: agents.totalExecutions,
      unit: '',
      trend: agents.totalExecutions > 0
        ? ((agents.totalExecutions - agents.totalFailures) / agents.totalExecutions) * 100
        : 0,
      trendLabel: 'success rate',
      category: 'efficiency',
    });
  }

  if (models) {
    // AI cost efficiency
    const baselineModelCost = models.totalRequests * BASELINE.modelCostPerRequest;
    const modelSavings = Math.max(0, baselineModelCost - models.totalCost);

    metrics.push({
      label: 'AI Cost Efficiency',
      value: modelSavings,
      unit: '$',
      trend: models.totalCost > 0
        ? ((BASELINE.modelCostPerRequest - (models.totalCost / Math.max(1, models.totalRequests))) / BASELINE.modelCostPerRequest) * 100
        : 0,
      trendLabel: 'per-request savings',
      category: 'cost',
    });
  }

  return metrics;
}

function computeBeforeAfter(
  tasks: TaskPerformanceDashboard | null,
  agents: AgentActivityDashboard | null,
  models: ModelUsageDashboard | null,
): BeforeAfterMetric[] {
  const metrics: BeforeAfterMetric[] = [];

  if (tasks) {
    metrics.push({
      label: 'Task Completion Rate',
      before: BASELINE.taskCompletionRate * 100,
      after: tasks.overallCompletionRate * 100,
      unit: '%',
      improvement: ((tasks.overallCompletionRate - BASELINE.taskCompletionRate) / BASELINE.taskCompletionRate) * 100,
    });

    metrics.push({
      label: 'Avg Execution Time',
      before: BASELINE.avgExecutionTimeMs / 1000,
      after: tasks.averageExecutionTimeMs / 1000,
      unit: 's',
      improvement: ((BASELINE.avgExecutionTimeMs - tasks.averageExecutionTimeMs) / BASELINE.avgExecutionTimeMs) * 100,
    });

    metrics.push({
      label: 'Cost per Task',
      before: BASELINE.avgCostPerTask,
      after: tasks.averageCost,
      unit: '$',
      improvement: ((BASELINE.avgCostPerTask - tasks.averageCost) / BASELINE.avgCostPerTask) * 100,
    });
  }

  if (agents) {
    metrics.push({
      label: 'Success Rate',
      before: BASELINE.agentSuccessRate * 100,
      after: agents.overallSuccessRate * 100,
      unit: '%',
      improvement: ((agents.overallSuccessRate - BASELINE.agentSuccessRate) / BASELINE.agentSuccessRate) * 100,
    });
  }

  if (models) {
    const currentCostPerReq = models.totalRequests > 0
      ? models.totalCost / models.totalRequests
      : BASELINE.modelCostPerRequest;

    metrics.push({
      label: 'AI Cost per Request',
      before: BASELINE.modelCostPerRequest,
      after: currentCostPerReq,
      unit: '$',
      improvement: ((BASELINE.modelCostPerRequest - currentCostPerReq) / BASELINE.modelCostPerRequest) * 100,
    });
  }

  return metrics;
}

export function useImpactDashboard() {
  const [state, setState] = useState<ImpactState>({
    loading: true,
    error: null,
    connectionStatus: 'disconnected',
    taskPerformance: null,
    agentActivity: null,
    modelUsage: null,
    systemHealth: null,
    impactMetrics: [],
    beforeAfter: [],
    lastUpdated: null,
  });

  const connRef = useRef<HubConnection | null>(null);

  // Recompute derived metrics whenever raw data changes
  const recompute = useCallback(
    (
      tasks: TaskPerformanceDashboard | null,
      agents: AgentActivityDashboard | null,
      models: ModelUsageDashboard | null,
    ) => ({
      impactMetrics: computeImpactMetrics(tasks, agents, models),
      beforeAfter: computeBeforeAfter(tasks, agents, models),
    }),
    [],
  );

  // Initial REST fetch
  const fetchAll = useCallback(async () => {
    setState((s) => ({ ...s, loading: true, error: null }));
    try {
      const [tasks, agents, models, health] = await Promise.all([
        api.getTaskPerformance() as Promise<TaskPerformanceDashboard>,
        api.getAgentActivity() as Promise<AgentActivityDashboard>,
        api.getModelUsage() as Promise<ModelUsageDashboard>,
        api.getSystemHealth() as Promise<SystemHealthDashboard>,
      ]);

      const derived = recompute(tasks, agents, models);

      setState((s) => ({
        ...s,
        loading: false,
        taskPerformance: tasks,
        agentActivity: agents,
        modelUsage: models,
        systemHealth: health,
        ...derived,
        lastUpdated: new Date().toISOString(),
      }));
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setState((s) => ({ ...s, loading: false, error: msg }));
    }
  }, [recompute]);

  useEffect(() => {
    queueMicrotask(() => fetchAll());
  }, [fetchAll]);

  // SignalR for real-time updates
  const handleModuleUpdate = useCallback(
    (update: DashboardUpdate) => {
      const now = new Date().toISOString();
      setState((s) => {
        const next = { ...s, lastUpdated: now };

        switch (update.module) {
          case 'task-performance':
            next.taskPerformance = update.payload as TaskPerformanceDashboard;
            break;
          case 'agent-activity':
            next.agentActivity = update.payload as AgentActivityDashboard;
            break;
          case 'model-usage':
            next.modelUsage = update.payload as ModelUsageDashboard;
            break;
          case 'system-health':
            next.systemHealth = update.payload as SystemHealthDashboard;
            break;
          default:
            return s;
        }

        const derived = recompute(next.taskPerformance, next.agentActivity, next.modelUsage);
        return { ...next, ...derived };
      });
    },
    [recompute],
  );

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    connRef.current = connection;

    connection.on('ModuleDashboardUpdate', handleModuleUpdate);

    connection.onreconnecting(() => {
      setState((s) => ({ ...s, connectionStatus: 'reconnecting' }));
    });

    connection.onreconnected(() => {
      setState((s) => ({ ...s, connectionStatus: 'connected' }));
      connection.invoke('SubscribeToModule', 'task-performance').catch(() => {});
      connection.invoke('SubscribeToModule', 'agent-activity').catch(() => {});
      connection.invoke('SubscribeToModule', 'model-usage').catch(() => {});
      connection.invoke('SubscribeToModule', 'system-health').catch(() => {});
    });

    connection.onclose(() => {
      setState((s) => ({ ...s, connectionStatus: 'disconnected' }));
    });

    queueMicrotask(() => setState((s) => ({ ...s, connectionStatus: 'connecting' })));

    connection
      .start()
      .then(async () => {
        setState((s) => ({ ...s, connectionStatus: 'connected' }));
        await connection.invoke('SubscribeToModule', 'task-performance');
        await connection.invoke('SubscribeToModule', 'agent-activity');
        await connection.invoke('SubscribeToModule', 'model-usage');
        await connection.invoke('SubscribeToModule', 'system-health');
      })
      .catch(() => {
        setState((s) => ({ ...s, connectionStatus: 'disconnected' }));
      });

    return () => {
      if (connection.state !== HubConnectionState.Disconnected) {
        connection.stop();
      }
    };
  }, [handleModuleUpdate]);

  return { ...state, refresh: fetchAll };
}

// ── Agent Activity ──────────────────────────────────────────────

export interface AgentActivityEntry {
  agentId: string;
  name: string;
  status: string;
  activeTasks: number;
  totalExecutions: number;
  failedExecutions: number;
  successRate: number;
  averageLatencyMs: number;
  capabilities: string[];
  lastActiveAtUtc: string;
}

export interface AgentActivityEvent {
  agentId: string;
  agentName: string;
  eventType: string;
  description: string;
  occurredAtUtc: string;
}

export interface AgentActivityDashboard {
  totalRegistered: number;
  activeAgents: number;
  idleAgents: number;
  drainingAgents: number;
  totalExecutions: number;
  totalFailures: number;
  overallSuccessRate: number;
  agents: AgentActivityEntry[];
  recentEvents: AgentActivityEvent[];
  generatedAtUtc: string;
}

// ── Task Performance ────────────────────────────────────────────

export interface TaskTypePerformance {
  taskType: string;
  total: number;
  completed: number;
  failed: number;
  completionRate: number;
  averageExecutionTimeMs: number;
  averageCost: number;
  p95ExecutionTimeMs: number;
}

export interface TaskPerformanceTrend {
  taskType: string;
  metricName: string;
  currentValue: number;
  previousValue: number;
  changePercent: number;
  trendDirection: string;
}

export interface TaskPerformanceDashboard {
  totalTasks: number;
  completedTasks: number;
  failedTasks: number;
  overallCompletionRate: number;
  averageExecutionTimeMs: number;
  averageCost: number;
  byTaskType: TaskTypePerformance[];
  trends: TaskPerformanceTrend[];
  generatedAtUtc: string;
}

// ── System Health ───────────────────────────────────────────────

export interface SystemAlert {
  alertId: string;
  severity: string;
  component: string;
  message: string;
  isAcknowledged: boolean;
  raisedAtUtc: string;
}

export interface ResourceUtilization {
  cpuPercent: number;
  memoryUsedBytes: number;
  memoryTotalBytes: number;
  memoryPercent: number;
  diskUsedBytes: number;
  diskTotalBytes: number;
  activeThreads: number;
  pendingWorkItems: number;
}

export interface HealthCheckResult {
  component: string;
  status: string;
  description: string;
  responseTimeMs: number;
  checkedAtUtc: string;
}

export interface SystemHealthDashboard {
  overallStatus: string;
  healthScore: number;
  cluster: {
    totalNodes: number;
    activeNodes: number;
    drainingNodes: number;
    offlineNodes: number;
    averageCpuPercent: number;
    averageMemoryPercent: number;
    totalActiveTasks: number;
    clusterStatus: string;
  };
  resources: ResourceUtilization;
  healthChecks: HealthCheckResult[];
  activeAlerts: SystemAlert[];
  generatedAtUtc: string;
}

// ── Unified Dashboard ───────────────────────────────────────────

export interface UnifiedDashboard {
  agentActivity: AgentActivityDashboard;
  systemHealth: SystemHealthDashboard;
  taskPerformance: TaskPerformanceDashboard;
  generatedAtUtc: string;
}

// ── Real-time update payload ────────────────────────────────────

export interface DashboardUpdate {
  module: string;
  updateType: string;
  payload: unknown;
  timestampUtc: string;
}

// ── Connection state ────────────────────────────────────────────

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

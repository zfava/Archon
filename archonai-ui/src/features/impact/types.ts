// ── Observability data from ControlPlane ──────────────────

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

export interface ModelUsageDashboard {
  totalModels: number;
  totalRequests: number;
  overallSuccessRate: number;
  averageLatencyMs: number;
  totalCost: number;
  models: ModelUsageEntry[];
  trends: ModelUsageTrend[];
  generatedAtUtc: string;
}

export interface ModelUsageEntry {
  provider: string;
  model: string;
  requestCount: number;
  successRate: number;
  averageLatencyMs: number;
  p95LatencyMs: number;
  totalCost: number;
  costPerRequest: number;
  accuracyRate: number;
  compositeScore: number;
  healthStatus: string;
  lastUsedAtUtc: string;
}

export interface ModelUsageTrend {
  provider: string;
  model: string;
  metricName: string;
  currentValue: number;
  previousValue: number;
  changePercent: number;
  trendDirection: string;
}

export interface SystemHealthDashboard {
  overallStatus: string;
  healthScore: number;
  resources: ResourceUtilization;
  generatedAtUtc: string;
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

// ── Computed impact metrics ──────────────────────────────

export interface ImpactMetric {
  label: string;
  value: number;
  unit: string;
  trend: number;       // percent change
  trendLabel: string;
  category: 'revenue' | 'cost' | 'efficiency';
}

export interface BeforeAfterMetric {
  label: string;
  before: number;
  after: number;
  unit: string;
  improvement: number; // percent improvement
}

export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

export interface DashboardUpdate {
  module: string;
  updateType: string;
  payload: unknown;
  timestampUtc: string;
}

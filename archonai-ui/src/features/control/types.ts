// ── Execution Modes ─────────────────────────────────────────────

export type ExecutionMode = 'observe' | 'recommend' | 'execute';

export interface ExecutionModeConfig {
  mode: ExecutionMode;
  label: string;
  description: string;
  behaviors: string[];
}

export const EXECUTION_MODES: ExecutionModeConfig[] = [
  {
    mode: 'observe',
    label: 'Observe',
    description: 'Agents monitor systems and surface insights. No actions are taken.',
    behaviors: [
      'Collects data from connected systems',
      'Identifies anomalies and patterns',
      'Logs observations to dashboard',
      'Zero automated actions',
    ],
  },
  {
    mode: 'recommend',
    label: 'Recommend',
    description: 'Agents analyze and recommend actions. Humans approve before execution.',
    behaviors: [
      'Everything in Observe, plus:',
      'Generates actionable recommendations',
      'Queues actions for human approval',
      'Estimates impact before execution',
    ],
  },
  {
    mode: 'execute',
    label: 'Execute',
    description: 'Agents take action within policy guardrails. Alerts on exceptions.',
    behaviors: [
      'Everything in Recommend, plus:',
      'Auto-executes within cost/risk limits',
      'Escalates policy violations',
      'Full audit trail for all actions',
    ],
  },
];

// ── Department Rules ────────────────────────────────────────────

export const DEPARTMENTS = [
  'Finance',
  'Sales',
  'Marketing',
  'Operations',
  'Support',
] as const;

export type Department = (typeof DEPARTMENTS)[number];

export type DepartmentApproval = 'require-approval' | 'auto-execute' | 'inherit';

export interface DepartmentRule {
  department: Department;
  approval: DepartmentApproval;
  maxAutoCost: number;
  maxAutoRisk: number;
}

// ── Platform Policy (mirrors backend PlatformPolicy) ────────────

export type PolicyType =
  | 'Security'
  | 'RateLimit'
  | 'ResourceQuota'
  | 'DataAccess'
  | 'Compliance'
  | 'Workflow'
  | 'Agent';

export interface PlatformPolicy {
  id: string;
  tenantId: string;
  name: string;
  description: string;
  policyType: PolicyType;
  targetResource: string;
  rules: Record<string, string>;
  isEnabled: boolean;
  priority: number;
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

// ── Security Metrics ────────────────────────────────────────────

export interface SecurityViolationSummary {
  policyName: string;
  category: string;
  violationCount: number;
  lastOccurredAtUtc: string;
}

export interface SecurityMetrics {
  totalEvaluations: number;
  totalViolations: number;
  agentPermissionDenials: number;
  dataAccessDenials: number;
  workflowLimitBreaches: number;
  activePolicies: number;
  topViolations: SecurityViolationSummary[];
  generatedAtUtc: string;
}

// ── Tenant ──────────────────────────────────────────────────────

export interface Tenant {
  id: string;
  name: string;
  displayName: string;
  status: string;
  tier: string;
  resourceQuota: {
    maxAgents: number;
    maxWorkflows: number;
    maxConcurrentExecutions: number;
    maxStorageBytes: number;
    maxEventsPerMinute: number;
  };
  metadata: Record<string, string>;
  createdAtUtc: string;
}

// ── Persisted Settings ──────────────────────────────────────────

export interface ControlSettings {
  executionMode: ExecutionMode;
  departmentRules: DepartmentRule[];
}

export const DEFAULT_SETTINGS: ControlSettings = {
  executionMode: 'recommend',
  departmentRules: DEPARTMENTS.map((dept) => ({
    department: dept,
    approval: 'inherit' as DepartmentApproval,
    maxAutoCost: 500,
    maxAutoRisk: 60,
  })),
};

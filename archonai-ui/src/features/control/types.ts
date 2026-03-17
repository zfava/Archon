// ── Execution Modes ─────────────────────────────────────────────

export type ExecutionMode = 'manual' | 'assisted' | 'autonomous';

export interface ExecutionModeConfig {
  mode: ExecutionMode;
  description: string;
}

export const EXECUTION_MODES: ExecutionModeConfig[] = [
  {
    mode: 'manual',
    description: 'All actions require explicit human approval before execution.',
  },
  {
    mode: 'assisted',
    description: 'Low-risk actions auto-execute. High-risk actions require approval.',
  },
  {
    mode: 'autonomous',
    description: 'All actions execute automatically. Alerts on policy violations.',
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
  executionMode: 'assisted',
  departmentRules: DEPARTMENTS.map((dept) => ({
    department: dept,
    approval: 'inherit' as DepartmentApproval,
    maxAutoCost: 500,
    maxAutoRisk: 60,
  })),
};

export interface ActionSafetyClassification {
  id: string;
  actionType: string;
  reversibility: 'Reversible' | 'Compensatable' | 'Irreversible';
  rollbackSupported: boolean;
  rollbackStrategy: 'None' | 'Automatic' | 'ManualTrigger' | 'OutOfBand' | 'Compensation';
  rollbackWindow: string | null;
  compensationDescription: string | null;
  operatorNotes: string | null;
  classifiedBy: string;
  classifiedAtUtc: string;
  safetySummary: string;
}

export interface RollbackAttempt {
  id: string;
  actionId: string;
  initiatedBy: string;
  status: 'InProgress' | 'Succeeded' | 'Failed' | 'Blocked';
  detail: string | null;
  error: string | null;
  initiatedAtUtc: string;
  completedAtUtc: string | null;
}

export interface GovernedActionRecord {
  id: string;
  tenantId: string;
  decisionId: string | null;
  workflowId: string | null;
  approvalGateId: string | null;
  actionType: string;
  description: string;
  safetyClassification: ActionSafetyClassification;
  status: string;
  executedBy: string;
  executedAtUtc: string;
  rollbackHistory: RollbackAttempt[];
  compensationOutcome: string | null;
  updatedAtUtc: string;
}

export interface RollbackSummary {
  tenantId: string;
  totalActions: number;
  reversible: number;
  compensatable: number;
  irreversible: number;
  rollbacksAttempted: number;
  rollbacksSucceeded: number;
  rollbacksFailed: number;
  withinRollbackWindow: number;
  windowExpired: number;
}

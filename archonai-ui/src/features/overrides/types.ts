export type OverrideAction =
  | 'PauseWorkflow'
  | 'ResumeWorkflow'
  | 'CancelAction'
  | 'ModifyStrategy'
  | 'Rollback';

export type OverrideStatus = 'Pending' | 'Applied' | 'RolledBack' | 'Failed';

export interface HumanOverrideEntry {
  id: string;
  workflowId: string;
  action: OverrideAction;
  status: OverrideStatus;
  reason: string;
  previousValue: string | null;
  newValue: string | null;
  performedBy: string;
  createdAtUtc: string;
  completedAtUtc: string | null;
}

export interface OverrideResult {
  success: boolean;
  overrideId: string;
  message: string;
  workflowState: string | null;
}

export interface OverrideLog {
  entries: HumanOverrideEntry[];
  totalCount: number;
  generatedAtUtc: string;
}

export const ACTION_LABELS: Record<OverrideAction, string> = {
  PauseWorkflow: 'Pause',
  ResumeWorkflow: 'Resume',
  CancelAction: 'Cancel',
  ModifyStrategy: 'Modify Strategy',
  Rollback: 'Rollback',
};

export const STATUS_LABELS: Record<OverrideStatus, string> = {
  Pending: 'Pending',
  Applied: 'Applied',
  RolledBack: 'Rolled Back',
  Failed: 'Failed',
};

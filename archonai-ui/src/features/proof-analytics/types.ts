export interface ProofEvent {
  id: string;
  tenantId: string;
  decisionId: string;
  workflowId: string | null;
  eventType: string;
  actor: string;
  detail: string | null;
  expectedValue: number | null;
  actualValue: number | null;
  variance: number | null;
  variancePercent: number | null;
  actionType: string | null;
  isSuccess: boolean | null;
  overrideReason: string | null;
  economicImpact: number | null;
  impactAttribution: string | null;
  occurredAtUtc: string;
}

export interface ProofTimelineSummary {
  totalEvents: number;
  hasOutcome: boolean;
  wasOverridden: boolean;
  wasReversed: boolean;
  predictedValue: number | null;
  actualValue: number | null;
  variance: number | null;
  variancePercent: number | null;
  finalAssessment: string | null;
  decisionToOutcomeDuration: string | null;
}

export interface ProofTimeline {
  decisionId: string;
  tenantId: string;
  decisionTitle: string;
  domain: string;
  events: ProofEvent[];
  summary: ProofTimelineSummary;
}

export interface PredictedVsActualEntry {
  decisionId: string;
  title: string;
  domain: string;
  predictedValue: number | null;
  actualValue: number | null;
  variance: number | null;
  variancePercent: number | null;
  direction: string;
  decisionCreatedAtUtc: string;
  outcomeObservedAtUtc: string | null;
}

export interface PredictedVsActualSummary {
  tenantId: string;
  domain: string | null;
  totalDecisions: number;
  withOutcomes: number;
  onTarget: number;
  overperformed: number;
  underperformed: number;
  meanVariancePercent: number;
  medianVariancePercent: number;
  totalPredictedValue: number;
  totalActualValue: number;
  totalVariance: number;
  accuracyRate: number;
  entries: PredictedVsActualEntry[];
}

export interface ApprovalConversionByType {
  actionType: string;
  requested: number;
  granted: number;
  denied: number;
  executed: number;
  approvalRate: number;
  executionRate: number;
}

export interface ApprovalConversionSummary {
  tenantId: string;
  totalApprovalRequests: number;
  granted: number;
  denied: number;
  executedAfterApproval: number;
  pendingExecution: number;
  approvalRate: number;
  executionConversionRate: number;
  meanApprovalLatency: string | null;
  byActionType: Record<string, ApprovalConversionByType>;
}

export interface ExecutionTrendBucket {
  periodStart: string;
  periodEnd: string;
  executions: number;
  successes: number;
  failures: number;
  successRate: number;
}

export interface ExecutionTrendSummary {
  tenantId: string;
  totalExecutions: number;
  successes: number;
  failures: number;
  successRate: number;
  buckets: ExecutionTrendBucket[];
}

export interface OverrideRateSummary {
  tenantId: string;
  totalDecisions: number;
  overrides: number;
  reversals: number;
  overrideRate: number;
  reversalRate: number;
  overrideReasonDistribution: Record<string, number>;
}

export interface TrustByActionType {
  actionType: string;
  totalDecisions: number;
  withOutcomes: number;
  accuracyRate: number;
  overrideRate: number;
  meanConfidence: number;
  meanVariancePercent: number;
  trustGrade: string;
}

export interface TrustAnalyticsSummary {
  tenantId: string;
  byActionType: TrustByActionType[];
}

export interface ProofDashboard {
  tenantId: string;
  predictedVsActual: PredictedVsActualSummary;
  approvalConversion: ApprovalConversionSummary;
  executionTrends: ExecutionTrendSummary;
  overrideRates: OverrideRateSummary;
  trustAnalytics: TrustAnalyticsSummary;
  generatedAtUtc: string;
}

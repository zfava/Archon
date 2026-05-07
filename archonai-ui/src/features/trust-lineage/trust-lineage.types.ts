// ── Trust Lineage API Response Contracts ──
// Derived from ArchonAI.Api.Endpoints.TrustVisibilityEndpoints

// ── Shared Enums (string unions matching backend .ToString() output) ──

export type ReversibilityLevel = 'Reversible' | 'Compensatable' | 'Irreversible';
export type RiskLevel = 'Low' | 'Medium' | 'High';
export type DecisionStatus = 'Proposed' | 'Approved' | 'InProgress' | 'Completed' | 'Cancelled';
export type ApprovalGateStatus = 'Pending' | 'Approved' | 'Denied';
export type ExecutionStatus = 'NotExecuted' | 'Succeeded' | 'Failed';
export type GovernedActionStatus = 'Pending' | 'Executing' | 'Succeeded' | 'Failed' | 'RolledBack';
export type OutcomeDirection = 'Positive' | 'Negative' | 'Neutral';
export type OutcomeAssessment = 'Accurate' | 'Overestimated' | 'Underestimated' | 'WildlyOff';

// ── Lineage Response ──

export interface TrustLineageDecision {
  readonly title: string;
  readonly domain: string;
  readonly objective: string | null;
  readonly reversibility: ReversibilityLevel;
  readonly riskLevel: RiskLevel;
  readonly confidence: number | null;
  readonly expectedValue: number | null;
  readonly status: DecisionStatus;
  readonly createdBy: string;
  readonly createdAtUtc: string;
  readonly requiresApproval: boolean;
}

export interface ApprovalGate {
  readonly id: string;
  readonly actionType: string;
  readonly resourceId: string;
  readonly requestedBy: string;
  readonly justification: string;
  readonly status: ApprovalGateStatus;
  readonly reviewedBy: string | null;
  readonly reviewNotes: string | null;
  readonly requestedAtUtc: string;
  readonly reviewedAtUtc: string | null;
  readonly executionStatus: ExecutionStatus;
  readonly executionError: string | null;
  readonly executedAtUtc: string | null;
}

export interface ActionSafetyClassification {
  readonly reversibility: ReversibilityLevel;
  readonly rollbackSupported: boolean;
  readonly rollbackStrategy: string;
  readonly rollbackWindow: number | null; // minutes
  readonly safetySummary: string;
}

export interface GovernedAction {
  readonly id: string;
  readonly actionType: string;
  readonly description: string;
  readonly status: GovernedActionStatus;
  readonly executedBy: string;
  readonly executedAtUtc: string;
  readonly safety: ActionSafetyClassification;
  readonly rollbackAttempts: number;
  readonly compensationOutcome: string | null;
  readonly approvalGateId: string | null;
}

export interface LineageOutcome {
  readonly expectedOutcomeSummary: string | null;
  readonly expectedValue: number | null;
  readonly confidenceAtPrediction: number;
  readonly actualOutcomeSummary: string | null;
  readonly actualValue: number | null;
  readonly valueVariance: number | null;
  readonly variancePercent: number | null;
  readonly direction: OutcomeDirection;
  readonly assessment: OutcomeAssessment;
  readonly recalibrationSignal: string | null;
  readonly createdAtUtc: string;
  readonly updatedAtUtc: string | null;
}

export interface ProofEvent {
  readonly eventType: string;
  readonly actor: string | null;
  readonly detail: string | null;
  readonly expectedValue: number | null;
  readonly actualValue: number | null;
  readonly isSuccess: boolean | null;
  readonly overrideReason: string | null;
  readonly economicImpact: number | null;
  readonly occurredAtUtc: string;
}

export interface ProofTimeline {
  readonly decisionTitle: string;
  readonly domain: string;
  readonly totalEvents: number;
  readonly events: readonly ProofEvent[];
  readonly summary: string | null;
}

export interface LineageSummary {
  readonly hasApproval: boolean;
  readonly hasExecution: boolean;
  readonly hasOutcome: boolean;
  readonly hasProofTrail: boolean;
  readonly allActionsReversible: boolean;
  readonly anyRollbackAttempted: boolean;
  readonly varianceWithinThreshold: boolean;
}

export interface TrustLineageResponse {
  readonly decisionId: string;
  readonly decision: TrustLineageDecision;
  readonly approvalGates: readonly ApprovalGate[];
  readonly governedActions: readonly GovernedAction[];
  readonly outcome: LineageOutcome | null;
  readonly proofTimeline: ProofTimeline | null;
  readonly statusHistory: readonly unknown[];
  readonly lineageSummary: LineageSummary;
}

// ── Posture Response ──

export interface RecentApprovals {
  readonly total: number;
  readonly approved: number;
  readonly denied: number;
  readonly approvalRate: number;
}

export interface GovernancePosture {
  readonly activePolicies: number;
  readonly totalPolicies: number;
  readonly pendingApprovals: number;
  readonly recentApprovals: RecentApprovals;
  readonly separationOfDutiesEnforced: boolean;
}

export interface SafetyPosture {
  readonly totalActions: number;
  readonly reversible: number;
  readonly compensatable: number;
  readonly irreversible: number;
  readonly reversibilityRate: number;
  readonly rollbacksAttempted: number;
  readonly rollbacksSucceeded: number;
  readonly rollbacksFailed: number;
  readonly rollbackSuccessRate: number;
  readonly withinRollbackWindow: number;
  readonly windowExpired: number;
}

export interface TrustTierPosture {
  readonly totalPolicies: number;
  readonly enabledPolicies: number;
  readonly actionsCovered: number;
  readonly requiresReversible: number;
}

export interface TrustPostureResponse {
  readonly tenantId: string;
  readonly generatedAtUtc: string;
  readonly governance: GovernancePosture;
  readonly safety: SafetyPosture;
  readonly trustTiers: TrustTierPosture;
  readonly outcomes: unknown;
  readonly proofAnalytics: unknown;
}

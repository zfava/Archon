/* ── Demo Feature Types ──────────────────────────────────────────── */

/** Matches the C# DemoPhase record in DemoEndpoints.cs */
export interface DemoPhase {
  name: string;
  output: string;
  latencyMs: number;
}

/** Matches the C# DemoEnvironmentReport record */
export interface DemoEnvironmentReport {
  readinessTier: string;
  readinessSummary: string;
  cloudProvidersActive: number;
  localProviderActive: boolean;
  defaultModel: string;
}

/** Matches the C# DemoResult record */
export interface DemoResult {
  scenario: string;
  phases: DemoPhase[];
  trustTier: string;
  approvalGateId: string | null;
  approvalStatus: string;
  overrideToken: string | null;
  trustLineageUrl: string | null;
  totalLatencyMs: number;
  environmentReport: DemoEnvironmentReport;
}

/** Matches the C# DemoRequest record */
export interface DemoRequest {
  scenario?: string;
  trustTierThreshold?: number;
  autoApprove?: boolean;
}

/* ── Parsed phase outputs ───────────────────────────────────────── */

export interface AiReasoningOutput {
  response: string;
  model: string;
  tokensUsed: number;
}

export interface PolicyEvaluationOutput {
  isAllowed: boolean;
  riskScore: number;
  confidenceScore: number;
  requiresApproval: boolean;
  approvalState: string;
  guardrailViolations: string[];
  reason: string;
}

export interface ApprovalGateOutput {
  approvalGateId: string;
  status: string;
  hasOverrideToken: boolean;
}

export interface GatedExecutionOutput {
  executed: boolean;
  success: boolean | null;
  error: string | null;
}

export interface OutcomeRecordingOutput {
  decisionId: string;
  expectedOutcomeSummary: string;
  confidenceAtPrediction: number;
}

/* ── Demo runner state ──────────────────────────────────────────── */

export type DemoScenario = 'finance-approval' | 'sales-anomaly' | 'ops-escalation';

export interface DemoRunnerState {
  result: DemoResult | null;
  isLoading: boolean;
  error: string | null;
  activePhaseIndex: number;
  isAnimating: boolean;
  isMockData: boolean;
}

/* ── Trust tier display ─────────────────────────────────────────── */

export interface TrustTierInfo {
  tier: string;
  label: string;
  description: string;
  example: string;
}

/* ── Comparison grid ────────────────────────────────────────────── */

export interface ComparisonRow {
  capability: string;
  archonai: string;
  workato: string;
  boomi: string;
  trayai: string;
}

/* ── Demo acts ──────────────────────────────────────────────────── */

export type DemoAct = 1 | 2 | 3 | 4 | 5;

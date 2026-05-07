export interface PolicyRuleResult {
  ruleName: string;
  ruleCategory: string;
  passed: boolean;
  riskContribution: number;
  detail: string;
}

export interface PolicyEvaluationResult {
  evaluationId: string;
  tenantId: string;
  subjectType: string;
  subjectId: string;
  isAllowed: boolean;
  riskScore: number;
  confidenceScore: number;
  requiresApproval: boolean;
  approvalState: string;
  manualOverrideState: string;
  approvalCheckpoint: string;
  guardrailViolations: string[];
  rulesEvaluated: PolicyRuleResult[];
  reason: string;
  evaluatedAtUtc: string;
}

export interface MemoryContextReference {
  memoryId: string;
  memoryType: string;
  source: string;
  contentSummary: string;
  relevanceScore: number;
  usageContext: string;
  retrievedAtUtc: string;
}

export interface LinkedArtifactReference {
  artifactType: string;
  artifactId: string;
  description: string;
  status: string;
  linkedAtUtc: string;
}

export interface RationaleChangeEvent {
  changeType: string;
  previousValue: string;
  newValue: string;
  reason: string;
  actor: string;
  occurredAtUtc: string;
}

export interface InspectedAlternative {
  id: string;
  title: string;
  rationale: string;
  pros: string[];
  cons: string[];
  estimatedConfidence: number | null;
  estimatedValue: number | null;
  isRecommended: boolean;
}

export interface DecisionRationaleBundle {
  decisionId: string;
  tenantId: string;
  title: string;
  domain: string;
  objective: string;
  assumptions: string[];
  constraints: string[];
  alternatives: InspectedAlternative[];
  recommendedOptionId: string;
  recommendationRationale: string;
  confidence: number;
  riskLevel: string;
  reversibility: string;
  policyEvaluation: PolicyEvaluationResult | null;
  memoryReferences: MemoryContextReference[];
  linkedArtifacts: LinkedArtifactReference[];
  changeHistory: RationaleChangeEvent[];
  createdBy: string;
  createdAtUtc: string;
  inspectedAtUtc: string;
}

export interface WorkflowStepDiagnostic {
  stepIndex: number;
  stepName: string;
  agentType: string;
  status: string;
  errorMessage: string | null;
  durationMs: number | null;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
}

export interface WorkflowFailureDiagnostics {
  workflowId: string;
  tenantId: string;
  workflowName: string;
  currentState: string;
  failureCategory: string;
  failureReason: string;
  failedStepName: string | null;
  failedStepIndex: number | null;
  stepDiagnostics: WorkflowStepDiagnostic[];
  policyEvaluations: PolicyEvaluationResult[];
  contextUsed: MemoryContextReference[];
  isRetryable: boolean;
  suggestedRemediation: string | null;
  relatedExceptions: LinkedArtifactReference[];
  failedAtUtc: string;
  inspectedAtUtc: string;
}

export interface InspectionSummary {
  subjectId: string;
  subjectType: string;
  title: string;
  status: string;
  domain: string;
  confidence: number | null;
  riskScore: number | null;
  hasPolicyViolations: boolean;
  hasFailures: boolean;
  createdAtUtc: string;
}

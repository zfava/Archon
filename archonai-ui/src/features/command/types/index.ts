export interface OperationalGoal {
  goalId: string;
  title: string;
  description: string;
  priority: 'Low' | 'Medium' | 'High' | 'Critical';
  source: 'BusinessSignal' | 'StateAnomaly' | 'PerformanceTrend' | 'Manual';
  status: 'Proposed' | 'Approved' | 'InProgress' | 'Completed' | 'Cancelled';
  expectedImpact: string;
  department: string;
  deadline: string;
  context: Record<string, string>;
  createdAtUtc: string;
}

export interface GoalGenerationResult {
  generatedGoals: OperationalGoal[];
  signalsAnalyzed: number;
  anomaliesDetected: number;
  trendsEvaluated: number;
  generatedAtUtc: string;
}

export interface GoalDashboard {
  totalGoals: number;
  proposedGoals: number;
  inProgressGoals: number;
  completedGoals: number;
  goalsByPriority: Record<string, number>;
  goalsBySource: Record<string, number>;
  recentGoals: OperationalGoal[];
  totalGenerationRuns: number;
  generatedAtUtc: string;
}

export interface ExpectedOutcome {
  overallSuccessProbability: number;
  predictedSuccess: boolean;
  predictedOutcomeLabel: string;
  confidence: number;
  totalNodes: number;
  criticalPathLength: number;
  parallelismDegree: number;
}

export interface SimulatedNodeResult {
  nodeId: string;
  nodeName: string;
  agentType: string;
  successProbability: number;
  estimatedDurationHours: number;
  estimatedCost: number;
  isOnCriticalPath: boolean;
  nodeRisks: string[];
}

export interface StrategySimulationResult {
  simulationId: string;
  graphId: string;
  strategy: string;
  expectedOutcome: ExpectedOutcome;
  riskScore: number;
  riskLevel: string;
  nodeResults: SimulatedNodeResult[];
  risks: string[];
  warnings: string[];
  estimatedTotalDurationHours: number;
  estimatedTotalCost: number;
  simulatedAtUtc: string;
}

export interface TaskGraphStrategyComparison {
  goalId: string;
  simulations: StrategySimulationResult[];
  recommendedSimulation: StrategySimulationResult;
  recommendationReason: string;
  comparedAtUtc: string;
}

export interface TaskGraphNode {
  nodeId: string;
  name: string;
  description: string;
  agentType: string;
  requiredInputs: Record<string, string>;
  expectedOutput: string;
  priority: number;
  estimatedDurationHours: number;
  status: string;
  createdAtUtc: string;
}

export interface TaskGraph {
  graphId: string;
  goalId: string;
  goalTitle: string;
  nodes: TaskGraphNode[];
  edges: { edgeId: string; sourceNodeId: string; targetNodeId: string }[];
  strategy: string;
  createdAtUtc: string;
}

export interface TaskGraphDispatchResult {
  graphId: string;
  workflowId: string;
  totalTasks: number;
  executionLayers: number;
  scheduledTaskIds: string[];
  dispatchedAtUtc: string;
}

export interface SimulationGuidedPlan {
  goal: OperationalGoal;
  selectedTaskGraph: TaskGraph;
  selectedSimulation: StrategySimulationResult;
  comparisonResult: TaskGraphStrategyComparison;
  selectedStrategy: string;
  expectedSuccessProbability: number;
  riskScore: number;
  planDecisionReason: string;
  plannedAtUtc: string;
}

// ── Outcome Evaluation (from Reasoner) ────────────────────────

export interface SuccessMetrics {
  successRate: number;
  accuracyScore: number;
  durationAccuracy: number;
  costAccuracy: number;
  riskPredictionAccuracy: number;
  totalNodes: number;
  succeededNodes: number;
  failedNodes: number;
  overallScore: number;
}

export interface ExpectedVsActual {
  expectedSuccessProbability: number;
  actualSuccess: boolean;
  expectedDurationHours: number;
  actualDurationHours: number;
  durationDeviationPercent: number;
  expectedCost: number;
  actualCost: number;
  costDeviationPercent: number;
  expectedRiskScore: number;
  riskPredictionCorrect: boolean;
}

export interface NodeEvaluationResult {
  nodeId: string;
  nodeName: string;
  agentType: string;
  predictedSuccessProbability: number;
  actualSuccess: boolean;
  predictedDurationHours: number;
  actualDurationHours: number;
  predictedCost: number;
  actualCost: number;
  wasOnCriticalPath: boolean;
  assessment: string;
}

export interface OutcomeEvaluationResult {
  evaluationId: string;
  graphId: string;
  goalId: string;
  strategy: string;
  successMetrics: SuccessMetrics;
  comparison: ExpectedVsActual;
  nodeEvaluations: NodeEvaluationResult[];
  insights: string[];
  recommendations: string[];
  overallAssessment: string;
  evaluatedAtUtc: string;
}

// ── Economic Evaluation (from Reasoner) ───────────────────────

export interface StrategyEvaluation {
  strategy: string;
  estimatedCost: number;
  expectedImpact: number;
  probabilityOfSuccess: number;
  estimatedExecutionTimeHours: number;
  economicScore: number;
  scoreBreakdown: string;
  factorScores: Record<string, number>;
  evaluatedAtUtc: string;
}

export interface EconomicEvaluationResult {
  evaluations: StrategyEvaluation[];
  bestStrategy: StrategyEvaluation;
  selectionRationale: string;
  weightsUsed: {
    costWeight: number;
    impactWeight: number;
    successProbabilityWeight: number;
    executionTimeWeight: number;
  };
  evaluatedAtUtc: string;
}

// ── Decision Summary (composite) ──────────────────────────────

export interface DecisionSummary {
  confidenceScore: number;
  riskScore: number;
  riskLevel: string;
  reasoning: string;
  insights: string[];
  recommendations: string[];
  assessment: string;
}

export type CommandPhase =
  | 'idle'
  | 'parsing'
  | 'planning'
  | 'simulating'
  | 'ready'
  | 'executing'
  | 'complete'
  | 'error';

export interface CommandResult {
  id: string;
  input: string;
  goal: OperationalGoal | null;
  plan: SimulationGuidedPlan | null;
  comparison: TaskGraphStrategyComparison | null;
  dispatch: TaskGraphDispatchResult | null;
  phase: CommandPhase;
  error: string | null;
  timestamp: string;
}

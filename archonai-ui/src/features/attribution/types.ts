// ── Goal ──────────────────────────────────────────────

export interface OperationalGoal {
  goalId: string;
  title: string;
  description: string;
  priority: string;
  source: string;
  status: string;
  expectedImpact: string;
  department: string;
  deadline: string;
  context: Record<string, string>;
  createdAtUtc: string;
}

// ── Task Graph ───────────────────────────────────────

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

// ── Strategy Simulation ──────────────────────────────

export interface ExpectedOutcome {
  overallSuccessProbability: number;
  predictedSuccess: boolean;
  predictedOutcomeLabel: string;
  confidence: number;
  totalNodes: number;
  criticalPathLength: number;
  parallelismDegree: number;
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

export interface SimulationGuidedPlan {
  goal: OperationalGoal;
  selectedTaskGraph: TaskGraph;
  selectedSimulation: StrategySimulationResult;
  comparisonResult: {
    goalId: string;
    simulations: StrategySimulationResult[];
    recommendedSimulation: StrategySimulationResult;
    recommendationReason: string;
    comparedAtUtc: string;
  };
  selectedStrategy: string;
  expectedSuccessProbability: number;
  riskScore: number;
  planDecisionReason: string;
  plannedAtUtc: string;
}

// ── Outcome Evaluation ───────────────────────────────

export interface OutcomeEvaluationResult {
  evaluationId: string;
  graphId: string;
  goalId: string;
  strategy: string;
  successMetrics: {
    successRate: number;
    accuracyScore: number;
    durationAccuracy: number;
    costAccuracy: number;
    riskPredictionAccuracy: number;
    totalNodes: number;
    succeededNodes: number;
    failedNodes: number;
    overallScore: number;
  };
  comparison: {
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
  };
  nodeEvaluations: NodeEvaluationResult[];
  insights: string[];
  recommendations: string[];
  overallAssessment: string;
  evaluatedAtUtc: string;
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

// ── Knowledge Graph ──────────────────────────────────

export interface KnowledgeGraphLink {
  from: string;
  fromType: string;
  relationship: string;
  to: string;
  toType: string;
}

// ── Attribution Chain (composed) ─────────────────────

export interface AttributionChain {
  goal: OperationalGoal;
  plan: SimulationGuidedPlan | null;
  graphs: TaskGraph[];
  evaluations: OutcomeEvaluationResult[];
  knowledgeLinks: KnowledgeGraphLink[];
}

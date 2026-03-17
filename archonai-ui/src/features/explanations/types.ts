export interface StrategyFactor {
  name: string;
  score: number;
  weight: number;
  impact: string;
  description: string;
}

export interface StrategyComparison {
  strategy: string;
  economicScore: number;
  successProbability: number;
  estimatedCost: number;
  estimatedDurationHours: number;
  whyNotChosen: string;
}

export interface StrategyExplanation {
  explanationId: string;
  goalId: string;
  goalTitle: string;
  chosenStrategy: string;
  economicScore: number;
  selectionRationale: string;
  factors: StrategyFactor[];
  alternatives: StrategyComparison[];
  generatedAtUtc: string;
}

export interface AgentFactor {
  name: string;
  value: string;
  impact: string;
  description: string;
}

export interface AgentAlternative {
  agentId: string;
  agentName: string;
  score: number;
  successRate: number;
  averageLatencyMs: number;
  averageCost: number;
  whyNotChosen: string;
}

export interface AgentExplanation {
  explanationId: string;
  requiredCapability: string;
  taskType: string | null;
  selectedAgentId: string;
  selectedAgentName: string;
  selectionScore: number;
  selectionReason: string;
  factors: AgentFactor[];
  alternatives: AgentAlternative[];
  generatedAtUtc: string;
}

export interface DecisionExplanation {
  explanationId: string;
  decisionType: string;
  summary: string;
  strategyExplanation: StrategyExplanation | null;
  agentExplanation: AgentExplanation | null;
  generatedAtUtc: string;
}

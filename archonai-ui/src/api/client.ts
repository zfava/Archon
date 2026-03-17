const API_BASE = import.meta.env.VITE_API_BASE_URL ?? '/api/v1';

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  });
  if (!res.ok) {
    const body = await res.text().catch(() => '');
    throw new Error(`API ${res.status}: ${body || res.statusText}`);
  }
  return res.json();
}

export const api = {
  // Goals
  generateGoals: () =>
    request('/goals/generate', { method: 'POST' }),

  getGoalDashboard: () =>
    request('/goals/dashboard'),

  getGoal: (goalId: string) =>
    request(`/goals/${goalId}`),

  getGoalsByStatus: (status: string) =>
    request(`/goals/by-status/${status}`),

  approveGoal: (goalId: string) =>
    request(`/goals/${goalId}/approve`, { method: 'POST' }),

  cancelGoal: (goalId: string, reason: string) =>
    request(`/goals/${goalId}/cancel`, {
      method: 'POST',
      body: JSON.stringify({ reason }),
    }),

  // Strategy Simulation
  simulateStrategy: (graphId: string, strategy: string) =>
    request('/strategy-simulation/simulate', {
      method: 'POST',
      body: JSON.stringify({ graphId, strategy }),
    }),

  compareStrategies: (graphId: string, strategies: string[]) =>
    request('/strategy-simulation/compare', {
      method: 'POST',
      body: JSON.stringify({ graphId, strategies }),
    }),

  simulateGoalStrategies: (goalId: string, strategies?: string[]) =>
    request('/strategy-simulation/simulate-goal', {
      method: 'POST',
      body: JSON.stringify({ goalId, strategies }),
    }),

  getGuidedPlan: (goalId: string, strategies?: string[]) =>
    request('/strategy-simulation/guided-plan', {
      method: 'POST',
      body: JSON.stringify({ goalId, strategies }),
    }),

  // Task Graphs
  buildTaskGraph: (goalId: string, strategy?: string) =>
    request('/task-graphs/build', {
      method: 'POST',
      body: JSON.stringify({ goalId, strategy }),
    }),

  dispatchGraph: (graphId: string) =>
    request(`/task-graphs/${graphId}/dispatch`, { method: 'POST' }),

  getGraph: (graphId: string) =>
    request(`/task-graphs/${graphId}`),

  getGraphLayers: (graphId: string) =>
    request(`/task-graphs/${graphId}/layers`),

  // Economics
  evaluateEconomics: (body: {
    objectiveTitle: string;
    objectiveDescription: string;
    candidateStrategies?: string[];
    constraints?: Record<string, string>;
    deadline?: string;
  }) =>
    request('/economics/evaluate', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  // Outcome
  evaluateOutcome: (body: unknown) =>
    request('/outcome-evaluation/evaluate', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
};

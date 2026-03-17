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

  getGraphsByGoal: (goalId: string) =>
    request(`/task-graphs/by-goal/${goalId}`),

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

  // ── Reasoner / Outcome Evaluation ──────────────────────────

  getOutcomeEvaluationsForGoal: (goalId: string) =>
    request(`/outcome-evaluation/goal/${goalId}`),

  getOutcomeEvaluationsForStrategy: (strategy: string) =>
    request(`/outcome-evaluation/strategy/${encodeURIComponent(strategy)}`),

  evaluateEconomicsSingle: (body: {
    objectiveTitle: string;
    objectiveDescription: string;
    strategy: string;
    constraints?: Record<string, string>;
    deadline?: string;
  }) =>
    request('/economics/evaluate-single', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  // ── Control Plane ──────────────────────────────────────────

  // Policies
  listPolicies: (tenantId?: string, policyType?: string) => {
    const params = new URLSearchParams();
    if (tenantId) params.set('tenantId', tenantId);
    if (policyType) params.set('policyType', policyType);
    const qs = params.toString();
    return request(`/control-plane/policies${qs ? `?${qs}` : ''}`);
  },

  createPolicy: (body: {
    tenantId: string;
    name: string;
    description: string;
    policyType: string;
    targetResource: string;
    rules: Record<string, string>;
    priority: number;
  }) =>
    request('/control-plane/policies', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updatePolicy: (policyId: string, body: {
    isEnabled: boolean;
    rules?: Record<string, string>;
    priority?: number;
  }) =>
    request(`/control-plane/policies/${policyId}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),

  deletePolicy: (policyId: string) =>
    request(`/control-plane/policies/${policyId}`, { method: 'DELETE' }),

  // Configuration (for persisting settings per tenant)
  getConfig: (tenantId: string, scope: string, key: string) =>
    request(`/control-plane/config/${tenantId}/${scope}/${key}`),

  setConfig: (body: {
    tenantId: string;
    scope: string;
    key: string;
    value: string;
    description?: string;
  }) =>
    request('/control-plane/config', {
      method: 'PUT',
      body: JSON.stringify(body),
    }),

  listConfigs: (tenantId?: string, scope?: string) => {
    const params = new URLSearchParams();
    if (tenantId) params.set('tenantId', tenantId);
    if (scope) params.set('scope', scope);
    const qs = params.toString();
    return request(`/control-plane/config${qs ? `?${qs}` : ''}`);
  },

  // Tenants
  listTenants: () =>
    request('/control-plane/tenants'),

  // Security metrics
  getSecurityMetrics: () =>
    request('/admin/security/metrics'),

  listSecurityPolicies: (category?: string) => {
    const qs = category ? `?category=${encodeURIComponent(category)}` : '';
    return request(`/admin/security/policies${qs}`);
  },

  // ── Audit Log ────────────────────────────────────────────

  getAuditStatus: () =>
    request('/audit/status'),

  queryAuditEntries: (params: {
    category?: string;
    subjectId?: string;
    resourceType?: string;
    fromUtc?: string;
    toUtc?: string;
    offset?: number;
    limit?: number;
  }) => {
    const qs = new URLSearchParams();
    if (params.category) qs.set('category', params.category);
    if (params.subjectId) qs.set('subjectId', params.subjectId);
    if (params.resourceType) qs.set('resourceType', params.resourceType);
    if (params.fromUtc) qs.set('fromUtc', params.fromUtc);
    if (params.toUtc) qs.set('toUtc', params.toUtc);
    if (params.offset !== undefined) qs.set('offset', String(params.offset));
    if (params.limit !== undefined) qs.set('limit', String(params.limit));
    const q = qs.toString();
    return request(`/audit/entries${q ? `?${q}` : ''}`);
  },

  getAuditEntry: (entryId: string) =>
    request(`/audit/entries/${entryId}`),

  verifyAuditIntegrity: (fromEntryId?: string) =>
    request('/audit/verify', {
      method: 'POST',
      body: JSON.stringify(fromEntryId ? { fromEntryId } : {}),
    }),

  // ── Observability ────────────────────────────────────────

  getUnifiedDashboard: () =>
    request('/control-plane/observability/unified'),

  getTaskPerformance: () =>
    request('/control-plane/observability/task-performance'),

  getAgentActivity: () =>
    request('/control-plane/observability/agent-activity'),

  getModelUsage: () =>
    request('/control-plane/observability/model-usage'),

  getSystemHealth: () =>
    request('/control-plane/observability/system-health'),

  // ── Integrations / Connectors ─────────────────────────
  getConnectorStatus: (path: string) =>
    request(path),

  connectIntegration: (connectorId: string) =>
    request(`/integrations/${connectorId}/connect`, { method: 'POST' }),

  disconnectIntegration: (connectorId: string) =>
    request(`/integrations/${connectorId}/disconnect`, { method: 'POST' }),

  listIntegrations: () =>
    request('/integrations'),

  // ── Onboarding ─────────────────────────────────────────
  deployOnboarding: (body: {
    connectedSystems: string[];
    businessType: string;
    automationLevel: string;
    departments: { name: string; level: string }[];
  }) =>
    request('/onboarding/deploy', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  // ── Explanations ─────────────────────────────────────
  explainStrategy: (body: {
    goalId: string;
    goalTitle: string;
    candidateStrategies: string[];
  }) =>
    request('/explanations/strategy', { method: 'POST', body: JSON.stringify(body) }),

  explainAgent: (body: {
    requiredCapability: string;
    taskType: string | null;
  }) =>
    request('/explanations/agent', { method: 'POST', body: JSON.stringify(body) }),

  explainDecision: (body: {
    goalId: string;
    goalTitle: string;
    candidateStrategies: string[];
    requiredCapability: string;
    taskType: string | null;
  }) =>
    request('/explanations/decision', { method: 'POST', body: JSON.stringify(body) }),

  // ── Human Overrides ──────────────────────────────────
  pauseWorkflow: (body: { workflowId: string; reason: string; performedBy: string }) =>
    request('/overrides/pause', { method: 'POST', body: JSON.stringify(body) }),

  resumeWorkflow: (body: { workflowId: string; reason: string; performedBy: string }) =>
    request('/overrides/resume', { method: 'POST', body: JSON.stringify(body) }),

  cancelOverrideAction: (body: {
    workflowId: string;
    taskId: string | null;
    reason: string;
    performedBy: string;
  }) =>
    request('/overrides/cancel', { method: 'POST', body: JSON.stringify(body) }),

  modifyStrategy: (body: {
    workflowId: string;
    previousStrategy: string;
    newStrategy: string;
    reason: string;
    performedBy: string;
  }) =>
    request('/overrides/modify-strategy', { method: 'POST', body: JSON.stringify(body) }),

  rollbackOverride: (body: {
    workflowId: string;
    overrideId: string;
    reason: string;
    performedBy: string;
  }) =>
    request('/overrides/rollback', { method: 'POST', body: JSON.stringify(body) }),

  getOverrideLog: (workflowId?: string, limit?: number) => {
    const params = new URLSearchParams();
    if (workflowId) params.set('workflowId', workflowId);
    if (limit !== undefined) params.set('limit', String(limit));
    const qs = params.toString();
    return request(`/overrides/log${qs ? `?${qs}` : ''}`);
  },

  deployOnboardingTemplate: (body: {
    templateId: string;
    connectedSystems: string[];
    businessType: string;
    automationLevel: string;
    departments: { name: string; level: string }[];
    agents: string[];
    workflows: { name: string; steps: string[] }[];
    strategies: string[];
  }) =>
    request('/onboarding/deploy-template', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
};

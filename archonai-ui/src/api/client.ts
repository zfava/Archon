import { ApiError, mapHttpError } from './errors';

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? '/api/v1';
const DEFAULT_TIMEOUT_MS = 30_000;

let _getAccessToken: (() => Promise<string | null>) | null = null;
let _onUnauthorized: (() => void) | null = null;

/** Wire auth into the API client. Called once from AuthProvider. */
export function configureApiAuth(
  getAccessToken: () => Promise<string | null>,
  onUnauthorized: () => void,
) {
  _getAccessToken = getAccessToken;
  _onUnauthorized = onUnauthorized;
}

/**
 * Core fetch wrapper.
 * - Attaches auth header automatically.
 * - Enforces a 30-second default timeout (overridable via options.signal).
 * - Normalizes all errors into ApiError instances.
 * - Dispatches 'auth:expired' CustomEvent on 401 responses.
 */
async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' };

  if (_getAccessToken) {
    const token = await _getAccessToken();
    if (token) headers['Authorization'] = `Bearer ${token}`;
  }

  // Combine caller signal (if any) with a default timeout signal
  const timeoutSignal = AbortSignal.timeout(DEFAULT_TIMEOUT_MS);
  const callerSignal = options?.signal;
  const signal = callerSignal
    ? AbortSignal.any([callerSignal, timeoutSignal])
    : timeoutSignal;

  let res: Response;
  try {
    res = await fetch(`${API_BASE}${path}`, {
      ...options,
      headers: { ...headers, ...(options?.headers as Record<string, string> | undefined) },
      signal,
    });
  } catch (err: unknown) {
    // Distinguish cancellation from timeout from network failure
    if (err instanceof DOMException && err.name === 'AbortError') {
      if (callerSignal?.aborted) {
        throw new ApiError('CANCELLED', 'Request was cancelled');
      }
      throw new ApiError('TIMEOUT', `Request timed out after ${DEFAULT_TIMEOUT_MS}ms`);
    }
    if (err instanceof Error && err.name === 'TimeoutError') {
      throw new ApiError('TIMEOUT', `Request timed out after ${DEFAULT_TIMEOUT_MS}ms`);
    }
    throw new ApiError(
      'NETWORK_ERROR',
      err instanceof Error ? err.message : 'Network request failed',
    );
  }

  // 401 — dispatch auth expiry event and invoke callback
  if (res.status === 401) {
    window.dispatchEvent(new CustomEvent('auth:expired'));
    _onUnauthorized?.();
    throw new ApiError('UNAUTHORIZED', 'Authentication required', 401);
  }

  // 403 — never silently fail
  if (res.status === 403) {
    const body = await res.text().catch(() => '');
    throw new ApiError(
      'FORBIDDEN',
      'You do not have permission to perform this action',
      403,
      body,
    );
  }

  // Other non-OK responses
  if (!res.ok) {
    const body = await res.text().catch(() => '');
    throw mapHttpError(res, body);
  }

  return res.json();
}

export const api = {
  // Goals
  generateGoals: (signal?: AbortSignal) =>
    request('/goals/generate', { method: 'POST', signal }),

  getGoalDashboard: (signal?: AbortSignal) =>
    request('/goals/dashboard', { signal }),

  getGoal: (goalId: string, signal?: AbortSignal) =>
    request(`/goals/${goalId}`, { signal }),

  getGoalsByStatus: (status: string, signal?: AbortSignal) =>
    request(`/goals/by-status/${status}`, { signal }),

  approveGoal: (goalId: string, signal?: AbortSignal) =>
    request(`/goals/${goalId}/approve`, { method: 'POST', signal }),

  cancelGoal: (goalId: string, reason: string, signal?: AbortSignal) =>
    request(`/goals/${goalId}/cancel`, {
      method: 'POST',
      body: JSON.stringify({ reason }),
      signal,
    }),

  // Strategy Simulation
  simulateStrategy: (graphId: string, strategy: string, signal?: AbortSignal) =>
    request('/strategy-simulation/simulate', {
      method: 'POST',
      body: JSON.stringify({ graphId, strategy }),
      signal,
    }),

  compareStrategies: (graphId: string, strategies: string[], signal?: AbortSignal) =>
    request('/strategy-simulation/compare', {
      method: 'POST',
      body: JSON.stringify({ graphId, strategies }),
      signal,
    }),

  simulateGoalStrategies: (goalId: string, strategies?: string[], signal?: AbortSignal) =>
    request('/strategy-simulation/simulate-goal', {
      method: 'POST',
      body: JSON.stringify({ goalId, strategies }),
      signal,
    }),

  getGuidedPlan: (goalId: string, strategies?: string[], signal?: AbortSignal) =>
    request('/strategy-simulation/guided-plan', {
      method: 'POST',
      body: JSON.stringify({ goalId, strategies }),
      signal,
    }),

  // Task Graphs
  buildTaskGraph: (goalId: string, strategy?: string, signal?: AbortSignal) =>
    request('/task-graphs/build', {
      method: 'POST',
      body: JSON.stringify({ goalId, strategy }),
      signal,
    }),

  dispatchGraph: (graphId: string, signal?: AbortSignal) =>
    request(`/task-graphs/${graphId}/dispatch`, { method: 'POST', signal }),

  getGraph: (graphId: string, signal?: AbortSignal) =>
    request(`/task-graphs/${graphId}`, { signal }),

  getGraphsByGoal: (goalId: string, signal?: AbortSignal) =>
    request(`/task-graphs/by-goal/${goalId}`, { signal }),

  getGraphLayers: (graphId: string, signal?: AbortSignal) =>
    request(`/task-graphs/${graphId}/layers`, { signal }),

  // Economics
  evaluateEconomics: (body: {
    objectiveTitle: string;
    objectiveDescription: string;
    candidateStrategies?: string[];
    constraints?: Record<string, string>;
    deadline?: string;
  }, signal?: AbortSignal) =>
    request('/economics/evaluate', {
      method: 'POST',
      body: JSON.stringify(body),
      signal,
    }),

  // Outcome
  evaluateOutcome: (body: unknown, signal?: AbortSignal) =>
    request('/outcome-evaluation/evaluate', {
      method: 'POST',
      body: JSON.stringify(body),
      signal,
    }),

  // ── Reasoner / Outcome Evaluation ──────────────────────────

  getOutcomeEvaluationsForGoal: (goalId: string, signal?: AbortSignal) =>
    request(`/outcome-evaluation/goal/${goalId}`, { signal }),

  getOutcomeEvaluationsForStrategy: (strategy: string, signal?: AbortSignal) =>
    request(`/outcome-evaluation/strategy/${encodeURIComponent(strategy)}`, { signal }),

  evaluateEconomicsSingle: (body: {
    objectiveTitle: string;
    objectiveDescription: string;
    strategy: string;
    constraints?: Record<string, string>;
    deadline?: string;
  }, signal?: AbortSignal) =>
    request('/economics/evaluate-single', {
      method: 'POST',
      body: JSON.stringify(body),
      signal,
    }),

  // ── Control Plane ──────────────────────────────────────────

  // Policies
  listPolicies: (tenantId?: string, policyType?: string, signal?: AbortSignal) => {
    const params = new URLSearchParams();
    if (tenantId) params.set('tenantId', tenantId);
    if (policyType) params.set('policyType', policyType);
    const qs = params.toString();
    return request(`/control-plane/policies${qs ? `?${qs}` : ''}`, { signal });
  },

  createPolicy: (body: {
    tenantId: string;
    name: string;
    description: string;
    policyType: string;
    targetResource: string;
    rules: Record<string, string>;
    priority: number;
  }, signal?: AbortSignal) =>
    request('/control-plane/policies', {
      method: 'POST',
      body: JSON.stringify(body),
      signal,
    }),

  updatePolicy: (policyId: string, body: {
    isEnabled: boolean;
    rules?: Record<string, string>;
    priority?: number;
  }, signal?: AbortSignal) =>
    request(`/control-plane/policies/${policyId}`, {
      method: 'PUT',
      body: JSON.stringify(body),
      signal,
    }),

  deletePolicy: (policyId: string, signal?: AbortSignal) =>
    request(`/control-plane/policies/${policyId}`, { method: 'DELETE', signal }),

  // Configuration (for persisting settings per tenant)
  getConfig: (tenantId: string, scope: string, key: string, signal?: AbortSignal) =>
    request(`/control-plane/config/${tenantId}/${scope}/${key}`, { signal }),

  setConfig: (body: {
    tenantId: string;
    scope: string;
    key: string;
    value: string;
    description?: string;
  }, signal?: AbortSignal) =>
    request('/control-plane/config', {
      method: 'PUT',
      body: JSON.stringify(body),
      signal,
    }),

  listConfigs: (tenantId?: string, scope?: string, signal?: AbortSignal) => {
    const params = new URLSearchParams();
    if (tenantId) params.set('tenantId', tenantId);
    if (scope) params.set('scope', scope);
    const qs = params.toString();
    return request(`/control-plane/config${qs ? `?${qs}` : ''}`, { signal });
  },

  // Tenants
  listTenants: (signal?: AbortSignal) =>
    request('/control-plane/tenants', { signal }),

  // Security metrics
  getSecurityMetrics: (signal?: AbortSignal) =>
    request('/admin/security/metrics', { signal }),

  listSecurityPolicies: (category?: string, signal?: AbortSignal) => {
    const qs = category ? `?category=${encodeURIComponent(category)}` : '';
    return request(`/admin/security/policies${qs}`, { signal });
  },

  // ── Audit Log ────────────────────────────────────────────

  getAuditStatus: (signal?: AbortSignal) =>
    request('/audit/status', { signal }),

  queryAuditEntries: (params: {
    category?: string;
    subjectId?: string;
    resourceType?: string;
    fromUtc?: string;
    toUtc?: string;
    offset?: number;
    limit?: number;
  }, signal?: AbortSignal) => {
    const qs = new URLSearchParams();
    if (params.category) qs.set('category', params.category);
    if (params.subjectId) qs.set('subjectId', params.subjectId);
    if (params.resourceType) qs.set('resourceType', params.resourceType);
    if (params.fromUtc) qs.set('fromUtc', params.fromUtc);
    if (params.toUtc) qs.set('toUtc', params.toUtc);
    if (params.offset !== undefined) qs.set('offset', String(params.offset));
    if (params.limit !== undefined) qs.set('limit', String(params.limit));
    const q = qs.toString();
    return request(`/audit/entries${q ? `?${q}` : ''}`, { signal });
  },

  getAuditEntry: (entryId: string, signal?: AbortSignal) =>
    request(`/audit/entries/${entryId}`, { signal }),

  verifyAuditIntegrity: (fromEntryId?: string, signal?: AbortSignal) =>
    request('/audit/verify', {
      method: 'POST',
      body: JSON.stringify(fromEntryId ? { fromEntryId } : {}),
      signal,
    }),

  // ── Observability ────────────────────────────────────────

  getUnifiedDashboard: (signal?: AbortSignal) =>
    request('/control-plane/observability/unified', { signal }),

  getTaskPerformance: (signal?: AbortSignal) =>
    request('/control-plane/observability/task-performance', { signal }),

  getAgentActivity: (signal?: AbortSignal) =>
    request('/control-plane/observability/agent-activity', { signal }),

  getModelUsage: (signal?: AbortSignal) =>
    request('/control-plane/observability/model-usage', { signal }),

  getSystemHealth: (signal?: AbortSignal) =>
    request('/control-plane/observability/system-health', { signal }),

  // ── Integrations / Connectors ─────────────────────────

  getConnectorStatus: (path: string, signal?: AbortSignal) =>
    request(path, { signal }),

  connectIntegration: (connectorId: string, signal?: AbortSignal) =>
    request(`/integrations/${connectorId}/connect`, { method: 'POST', signal }),

  disconnectIntegration: (connectorId: string, signal?: AbortSignal) =>
    request(`/integrations/${connectorId}/disconnect`, { method: 'POST', signal }),

  listIntegrations: (signal?: AbortSignal) =>
    request('/integrations', { signal }),

  // ── Onboarding ─────────────────────────────────────────

  deployOnboarding: (body: {
    connectedSystems: string[];
    businessType: string;
    automationLevel: string;
    departments: { name: string; level: string }[];
  }, signal?: AbortSignal) =>
    request('/onboarding/deploy', {
      method: 'POST',
      body: JSON.stringify(body),
      signal,
    }),

  // ── Explanations ─────────────────────────────────────

  explainStrategy: (body: {
    goalId: string;
    goalTitle: string;
    candidateStrategies: string[];
  }, signal?: AbortSignal) =>
    request('/explanations/strategy', { method: 'POST', body: JSON.stringify(body), signal }),

  explainAgent: (body: {
    requiredCapability: string;
    taskType: string | null;
  }, signal?: AbortSignal) =>
    request('/explanations/agent', { method: 'POST', body: JSON.stringify(body), signal }),

  explainDecision: (body: {
    goalId: string;
    goalTitle: string;
    candidateStrategies: string[];
    requiredCapability: string;
    taskType: string | null;
  }, signal?: AbortSignal) =>
    request('/explanations/decision', { method: 'POST', body: JSON.stringify(body), signal }),

  // ── Human Overrides ──────────────────────────────────

  pauseWorkflow: (body: { workflowId: string; reason: string; performedBy: string }, signal?: AbortSignal) =>
    request('/overrides/pause', { method: 'POST', body: JSON.stringify(body), signal }),

  resumeWorkflow: (body: { workflowId: string; reason: string; performedBy: string }, signal?: AbortSignal) =>
    request('/overrides/resume', { method: 'POST', body: JSON.stringify(body), signal }),

  cancelOverrideAction: (body: {
    workflowId: string;
    taskId: string | null;
    reason: string;
    performedBy: string;
  }, signal?: AbortSignal) =>
    request('/overrides/cancel', { method: 'POST', body: JSON.stringify(body), signal }),

  modifyStrategy: (body: {
    workflowId: string;
    previousStrategy: string;
    newStrategy: string;
    reason: string;
    performedBy: string;
  }, signal?: AbortSignal) =>
    request('/overrides/modify-strategy', { method: 'POST', body: JSON.stringify(body), signal }),

  rollbackOverride: (body: {
    workflowId: string;
    overrideId: string;
    reason: string;
    performedBy: string;
  }, signal?: AbortSignal) =>
    request('/overrides/rollback', { method: 'POST', body: JSON.stringify(body), signal }),

  getOverrideLog: (workflowId?: string, limit?: number, signal?: AbortSignal) => {
    const params = new URLSearchParams();
    if (workflowId) params.set('workflowId', workflowId);
    if (limit !== undefined) params.set('limit', String(limit));
    const qs = params.toString();
    return request(`/overrides/log${qs ? `?${qs}` : ''}`, { signal });
  },

  // ── Decisions ──────────────────────────────────────────

  listDecisions: (params?: { domain?: string; status?: string; limit?: number }, signal?: AbortSignal) => {
    const qs = new URLSearchParams();
    if (params?.domain) qs.set('domain', params.domain);
    if (params?.status) qs.set('status', params.status);
    if (params?.limit !== undefined) qs.set('limit', String(params.limit));
    const q = qs.toString();
    return request(`/decisions${q ? `?${q}` : ''}`, { signal });
  },

  getDecision: (decisionId: string, signal?: AbortSignal) =>
    request(`/decisions/${decisionId}`, { signal }),

  createDecision: (body: {
    title: string;
    domain: string;
    objective?: string;
    constraints?: string[];
    assumptions?: string[];
    alternatives?: {
      title: string;
      rationale: string;
      pros?: string[];
      cons?: string[];
      estimatedConfidence?: number;
      estimatedValue?: number;
    }[];
    recommendedOptionId?: string;
    confidence?: number;
    reversibility?: string;
    riskLevel?: string;
    expectedValue?: number;
    requiresApproval?: boolean;
  }, signal?: AbortSignal) =>
    request('/decisions', { method: 'POST', body: JSON.stringify(body), signal }),

  updateDecisionStatus: (decisionId: string, status: string, detail?: string, signal?: AbortSignal) =>
    request(`/decisions/${decisionId}/status`, {
      method: 'PUT',
      body: JSON.stringify({ status, detail }),
      signal,
    }),

  linkDecisionArtifact: (decisionId: string, body: {
    artifactType: string;
    artifactId: string;
    description?: string;
  }, signal?: AbortSignal) =>
    request(`/decisions/${decisionId}/links`, {
      method: 'POST',
      body: JSON.stringify(body),
      signal,
    }),

  getDecisionHistory: (decisionId: string, signal?: AbortSignal) =>
    request(`/decisions/${decisionId}/history`, { signal }),

  // ── Financial Consequence ─────────────────────────────

  getFinancialConsequence: (decisionId: string, signal?: AbortSignal) =>
    request(`/decisions/${decisionId}/financial-consequence`, { signal }),

  attachFinancialConsequence: (decisionId: string, body: {
    expectedRevenueImpactLow?: number;
    expectedRevenueImpactHigh?: number;
    expectedCostImpactLow?: number;
    expectedCostImpactHigh?: number;
    expectedMarginImpact?: number;
    expectedCashTimingImpact?: string;
    laborImpact?: string;
    downsideRisk?: number;
    upsidePotential?: number;
    confidenceAdjustment?: number;
    roiEstimateLow?: number;
    roiEstimateHigh?: number;
    breakEvenEstimate?: string;
    assumptions?: string[];
    notes?: string;
  }, signal?: AbortSignal) =>
    request(`/decisions/${decisionId}/financial-consequence`, {
      method: 'POST',
      body: JSON.stringify(body),
      signal,
    }),

  updateFinancialConsequence: (decisionId: string, body: {
    expectedRevenueImpactLow?: number;
    expectedRevenueImpactHigh?: number;
    expectedCostImpactLow?: number;
    expectedCostImpactHigh?: number;
    expectedMarginImpact?: number;
    expectedCashTimingImpact?: string;
    laborImpact?: string;
    downsideRisk?: number;
    upsidePotential?: number;
    confidenceAdjustment?: number;
    roiEstimateLow?: number;
    roiEstimateHigh?: number;
    breakEvenEstimate?: string;
    assumptions?: string[];
    notes?: string;
  }, signal?: AbortSignal) =>
    request(`/decisions/${decisionId}/financial-consequence`, {
      method: 'PUT',
      body: JSON.stringify(body),
      signal,
    }),

  // ── Trust Tiers ───────────────────────────────────────

  listTrustTierPolicies: (signal?: AbortSignal) =>
    request('/trust-tiers/policies', { signal }),

  setTrustTierPolicy: (body: {
    id?: string;
    actionScope: string;
    maxTier: string;
    confidenceThreshold?: number;
    valueCeiling?: number;
    requireReversible?: boolean;
    description?: string;
    isEnabled?: boolean;
  }, signal?: AbortSignal) =>
    request('/trust-tiers/policies', {
      method: 'POST',
      body: JSON.stringify(body),
      signal,
    }),

  deleteTrustTierPolicy: (policyId: string, signal?: AbortSignal) =>
    request(`/trust-tiers/policies/${policyId}`, { method: 'DELETE', signal }),

  evaluateTrustTier: (body: {
    actionScope: string;
    requestedTier: string;
    confidence?: number;
    value?: number;
    reversible?: boolean;
  }, signal?: AbortSignal) =>
    request('/trust-tiers/evaluate', {
      method: 'POST',
      body: JSON.stringify(body),
      signal,
    }),

  getTrustTierMap: (signal?: AbortSignal) =>
    request('/trust-tiers/map', { signal }),

  getEffectiveTier: (actionScope: string, signal?: AbortSignal) =>
    request(`/trust-tiers/effective/${encodeURIComponent(actionScope)}`, { signal }),

  // ── Outcome Learning ──────────────────────────────────

  recordExpectedOutcome: (body: {
    decisionId: string;
    expectedSummary?: string;
    expectedValue?: number;
    confidenceAtPrediction: number;
    expectedTimeframe?: string;
  }, signal?: AbortSignal) =>
    request('/outcomes/expected', {
      method: 'POST',
      body: JSON.stringify(body),
      signal,
    }),

  recordActualOutcome: (body: {
    decisionId: string;
    actualSummary?: string;
    actualValue?: number;
    rootCause?: string;
    notes?: string;
  }, signal?: AbortSignal) =>
    request('/outcomes/actual', {
      method: 'POST',
      body: JSON.stringify(body),
      signal,
    }),

  getOutcome: (decisionId: string, signal?: AbortSignal) =>
    request(`/outcomes/${decisionId}`, { signal }),

  listOutcomes: (params?: Record<string, string | number>, signal?: AbortSignal) => {
    const qs = params ? new URLSearchParams(Object.entries(params).map(([k, v]) => [k, String(v)])).toString() : '';
    return request(`/outcomes${qs ? `?${qs}` : ''}`, { signal });
  },

  getCalibrationSummary: (params?: Record<string, string>, signal?: AbortSignal) =>
    request('/outcomes/calibration' + (params ? '?' + new URLSearchParams(params).toString() : ''), { signal }),

  // ── Enterprise Memory ─────────────────────────────────

  storeMemory: (body: {
    layer: string;
    subject: string;
    content: string;
    category?: string;
    metadata?: Record<string, string>;
    linkedEntities?: { entityType: string; entityId: string; relationship: string }[];
    tags?: string[];
    importance?: number;
    expiresAtUtc?: string;
  }, signal?: AbortSignal) =>
    request('/enterprise-memory', {
      method: 'POST',
      body: JSON.stringify(body),
      signal,
    }),

  getMemory: (recordId: string, signal?: AbortSignal) =>
    request(`/enterprise-memory/${recordId}`, { signal }),

  queryMemory: (params?: Record<string, string | number>, signal?: AbortSignal) => {
    const qs = params ? new URLSearchParams(Object.entries(params).map(([k, v]) => [k, String(v)])).toString() : '';
    return request(`/enterprise-memory${qs ? `?${qs}` : ''}`, { signal });
  },

  getEntityMemory: (entityType: string, entityId: string, signal?: AbortSignal) =>
    request(`/enterprise-memory/entity/${encodeURIComponent(entityType)}/${encodeURIComponent(entityId)}`, { signal }),

  getMemoryTimeline: (params?: Record<string, string | number>, signal?: AbortSignal) =>
    request('/enterprise-memory/timeline' + (params ? '?' + new URLSearchParams(Object.entries(params).map(([k, v]) => [k, String(v)])).toString() : ''), { signal }),

  // ── Operational Twin ──────────────────────────────────

  listTwinEntities: (params?: Record<string, string>, signal?: AbortSignal) =>
    request('/twin/entities' + (params ? '?' + new URLSearchParams(params).toString() : ''), { signal }),

  getTwinOverview: (signal?: AbortSignal) =>
    request('/twin/overview', { signal }),

  getTwinEntityDeps: (entityId: string, signal?: AbortSignal) =>
    request(`/twin/entities/${entityId}/dependencies`, { signal }),

  getTwinEntityKpis: (entityId: string, signal?: AbortSignal) =>
    request(`/twin/entities/${entityId}/kpis`, { signal }),

  getTwinEntityLinks: (entityId: string, signal?: AbortSignal) =>
    request(`/twin/entities/${entityId}/links`, { signal }),

  listTwinBottlenecks: (params?: Record<string, string>, signal?: AbortSignal) =>
    request('/twin/bottlenecks' + (params ? '?' + new URLSearchParams(params).toString() : ''), { signal }),

  // ── Scenarios ───────────────────────────────────────────

  createScenario: (body: {
    title: string;
    description?: string;
    type: string;
    assumptions?: { name: string; currentValue: string; proposedValue: string; unit?: string; rationale?: string }[];
    linkedKpiIds?: string[];
    linkedDecisionIds?: string[];
    linkedEntityIds?: string[];
  }, signal?: AbortSignal) =>
    request('/scenarios', { method: 'POST', body: JSON.stringify(body), signal }),

  updateScenarioAssumptions: (scenarioId: string, body: {
    assumptions: { name: string; currentValue: string; proposedValue: string; unit?: string; rationale?: string }[];
  }, signal?: AbortSignal) =>
    request(`/scenarios/${scenarioId}/assumptions`, { method: 'PUT', body: JSON.stringify(body), signal }),

  getScenario: (scenarioId: string, signal?: AbortSignal) =>
    request(`/scenarios/${scenarioId}`, { signal }),

  listScenarios: (params?: Record<string, string>, signal?: AbortSignal) => {
    const qs = params ? new URLSearchParams(params).toString() : '';
    return request(`/scenarios${qs ? `?${qs}` : ''}`, { signal });
  },

  compareScenarios: (scenarioIds: string[], signal?: AbortSignal) =>
    request('/scenarios/compare', { method: 'POST', body: JSON.stringify({ scenarioIds }), signal }),

  deleteScenario: (scenarioId: string, signal?: AbortSignal) =>
    request(`/scenarios/${scenarioId}`, { method: 'DELETE', signal }),

  // ── Exception Intelligence ──────────────────────────────

  raiseException: (body: {
    category: string;
    severity: string;
    title: string;
    description?: string;
    domain?: string;
    urgency?: number;
    economicImpactEstimate?: number;
    confidence?: number;
    escalationLevel?: string;
    assignedTo?: string;
    escalationPath?: string;
    linkedArtifacts?: { artifactType: string; artifactId: string; label?: string }[];
    recommendedAction?: { actionType: string; description: string; targetArtifactType?: string; targetArtifactId?: string; confidence?: string };
  }, signal?: AbortSignal) =>
    request('/exceptions', { method: 'POST', body: JSON.stringify(body), signal }),

  listExceptions: (params?: Record<string, string>, signal?: AbortSignal) => {
    const qs = params ? new URLSearchParams(params).toString() : '';
    return request(`/exceptions${qs ? `?${qs}` : ''}`, { signal });
  },

  getException: (exceptionId: string, signal?: AbortSignal) =>
    request(`/exceptions/${exceptionId}`, { signal }),

  updateExceptionStatus: (exceptionId: string, body: { status: string; assignedTo?: string }, signal?: AbortSignal) =>
    request(`/exceptions/${exceptionId}/status`, { method: 'PUT', body: JSON.stringify(body), signal }),

  setRecommendedAction: (exceptionId: string, body: {
    actionType: string; description: string;
    targetArtifactType?: string; targetArtifactId?: string; confidence?: string;
  }, signal?: AbortSignal) =>
    request(`/exceptions/${exceptionId}/recommended-action`, { method: 'POST', body: JSON.stringify(body), signal }),

  getExceptionSummary: (signal?: AbortSignal) =>
    request('/exceptions/summary', { signal }),

  getExceptionPrioritized: (limit?: number, signal?: AbortSignal) =>
    request(`/exceptions/prioritized${limit ? `?limit=${limit}` : ''}`, { signal }),

  // ── Hero Workflows ──────────────────────────────────────

  getHeroWorkflowCatalog: (signal?: AbortSignal) =>
    request('/hero-workflows/catalog', { signal }),

  getHeroWorkflowDefinition: (workflowType: string, signal?: AbortSignal) =>
    request(`/hero-workflows/catalog/${encodeURIComponent(workflowType)}`, { signal }),

  startHeroWorkflow: (body: {
    workflowType: string;
    title: string;
    inputs?: Record<string, string>;
  }, signal?: AbortSignal) =>
    request('/hero-workflows', { method: 'POST', body: JSON.stringify(body), signal }),

  advanceHeroWorkflow: (workflowId: string, inputs?: Record<string, string>, signal?: AbortSignal) =>
    request(`/hero-workflows/${workflowId}/advance`, {
      method: 'POST',
      body: JSON.stringify({ inputs }),
      signal,
    }),

  getHeroWorkflow: (workflowId: string, signal?: AbortSignal) =>
    request(`/hero-workflows/${workflowId}`, { signal }),

  listHeroWorkflows: (params?: { workflowType?: string; status?: string; limit?: number }, signal?: AbortSignal) => {
    const qs = new URLSearchParams();
    if (params?.workflowType) qs.set('workflowType', params.workflowType);
    if (params?.status) qs.set('status', params.status);
    if (params?.limit !== undefined) qs.set('limit', String(params.limit));
    const q = qs.toString();
    return request(`/hero-workflows${q ? `?${q}` : ''}`, { signal });
  },

  cancelHeroWorkflow: (workflowId: string, signal?: AbortSignal) =>
    request(`/hero-workflows/${workflowId}/cancel`, { method: 'POST', signal }),

  // ── Policy Simulation / Dry-Run ────────────────────────────

  runSimulation: (body: {
    actionType: string;
    actionScope?: string;
    title: string;
    domain?: string;
    objective?: string;
    riskLevel?: string;
    reversibility?: string;
    confidence?: number;
    expectedValue?: number;
    revenueImpactLow?: number;
    revenueImpactHigh?: number;
    costImpactLow?: number;
    costImpactHigh?: number;
    downsideRisk?: number;
    upsidePotential?: number;
    requestedTier?: string;
    workflowType?: string;
  }, signal?: AbortSignal) =>
    request('/policy-simulation/simulate', { method: 'POST', body: JSON.stringify(body), signal }),

  getSimulation: (simulationId: string, signal?: AbortSignal) =>
    request(`/policy-simulation/${simulationId}`, { signal }),

  listSimulations: (limit?: number, signal?: AbortSignal) =>
    request(`/policy-simulation${limit ? `?limit=${limit}` : ''}`, { signal }),

  // ── Proof Analytics ──────────────────────────────────────

  getProofDashboard: (domain?: string, signal?: AbortSignal) =>
    request(`/proof-analytics/dashboard${domain ? `?domain=${encodeURIComponent(domain)}` : ''}`, { signal }),

  getProofTimeline: (decisionId: string, signal?: AbortSignal) =>
    request(`/proof-analytics/timeline/${decisionId}`, { signal }),

  getProofWorkflowTimelines: (workflowId: string, signal?: AbortSignal) =>
    request(`/proof-analytics/workflow/${workflowId}/timelines`, { signal }),

  getProofPredictedVsActual: (params?: { domain?: string; limit?: number }, signal?: AbortSignal) => {
    const qs = new URLSearchParams();
    if (params?.domain) qs.set('domain', params.domain);
    if (params?.limit !== undefined) qs.set('limit', String(params.limit));
    const q = qs.toString();
    return request(`/proof-analytics/predicted-vs-actual${q ? `?${q}` : ''}`, { signal });
  },

  getProofApprovalConversion: (signal?: AbortSignal) =>
    request('/proof-analytics/approval-conversion', { signal }),

  getProofExecutionTrends: (buckets?: number, signal?: AbortSignal) =>
    request(`/proof-analytics/execution-trends${buckets ? `?buckets=${buckets}` : ''}`, { signal }),

  getProofOverrideRates: (signal?: AbortSignal) =>
    request('/proof-analytics/override-rates', { signal }),

  getProofTrustAnalytics: (signal?: AbortSignal) =>
    request('/proof-analytics/trust-analytics', { signal }),

  recordProofEvent: (body: {
    decisionId: string;
    workflowId?: string;
    eventType: string;
    detail?: string;
    expectedValue?: number;
    actualValue?: number;
    variance?: number;
    variancePercent?: number;
    actionType?: string;
    isSuccess?: boolean;
    overrideReason?: string;
    economicImpact?: number;
    impactAttribution?: string;
  }, signal?: AbortSignal) =>
    request('/proof-analytics/events', { method: 'POST', body: JSON.stringify(body), signal }),

  // ── Action Safety & Rollback ─────────────────────────────

  getActionSafetyClassifications: (signal?: AbortSignal) =>
    request('/action-safety/classifications', { signal }),

  getActionSafetyClassification: (actionType: string, signal?: AbortSignal) =>
    request(`/action-safety/classifications/${encodeURIComponent(actionType)}`, { signal }),

  setActionSafetyClassification: (body: {
    actionType: string;
    reversibility: string;
    rollbackSupported: boolean;
    rollbackStrategy: string;
    rollbackWindowMinutes?: number;
    compensationDescription?: string;
    operatorNotes?: string;
  }, signal?: AbortSignal) =>
    request('/action-safety/classifications', { method: 'PUT', body: JSON.stringify(body), signal }),

  getGovernedActions: (limit?: number, signal?: AbortSignal) =>
    request(`/action-safety/actions${limit ? `?limit=${limit}` : ''}`, { signal }),

  getGovernedAction: (actionId: string, signal?: AbortSignal) =>
    request(`/action-safety/actions/${actionId}`, { signal }),

  recordGovernedAction: (body: {
    actionType: string;
    description: string;
    decisionId?: string;
    workflowId?: string;
    approvalGateId?: string;
  }, signal?: AbortSignal) =>
    request('/action-safety/actions', { method: 'POST', body: JSON.stringify(body), signal }),

  triggerRollback: (actionId: string, signal?: AbortSignal) =>
    request(`/action-safety/actions/${actionId}/rollback`, { method: 'POST', signal }),

  getActionSafetySummary: (signal?: AbortSignal) =>
    request('/action-safety/summary', { signal }),

  // ── Operator Inspection & Diagnostics ──────────────────

  getInspectionSummaries: (subjectType?: string, domain?: string, limit?: number, signal?: AbortSignal) => {
    const params = new URLSearchParams();
    if (subjectType) params.set('subjectType', subjectType);
    if (domain) params.set('domain', domain);
    if (limit) params.set('limit', limit.toString());
    const qs = params.toString();
    return request(`/inspection/summaries${qs ? `?${qs}` : ''}`, { signal });
  },

  inspectDecisionRationale: (decisionId: string, signal?: AbortSignal) =>
    request(`/inspection/decisions/${decisionId}/rationale`, { signal }),

  inspectPolicyEvaluation: (subjectType: string, subjectId: string, signal?: AbortSignal) =>
    request(`/inspection/policy/${encodeURIComponent(subjectType)}/${encodeURIComponent(subjectId)}`, { signal }),

  inspectMemoryReferences: (subjectType: string, subjectId: string, signal?: AbortSignal) =>
    request(`/inspection/memory/${encodeURIComponent(subjectType)}/${encodeURIComponent(subjectId)}`, { signal }),

  inspectWorkflowDiagnostics: (workflowId: string, signal?: AbortSignal) =>
    request(`/inspection/workflows/${workflowId}/diagnostics`, { signal }),

  // ── Executive Command ───────────────────────────────────

  getExecutiveCommandSummary: (signal?: AbortSignal) =>
    request('/executive-command/summary', { signal }),

  deployOnboardingTemplate: (body: {
    templateId: string;
    connectedSystems: string[];
    businessType: string;
    automationLevel: string;
    departments: { name: string; level: string }[];
    agents: string[];
    workflows: { name: string; steps: string[] }[];
    strategies: string[];
  }, signal?: AbortSignal) =>
    request('/onboarding/deploy-template', {
      method: 'POST',
      body: JSON.stringify(body),
      signal,
    }),
};

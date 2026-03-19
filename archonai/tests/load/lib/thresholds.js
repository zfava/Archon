// ArchonAI k6 Threshold Definitions
// Centralized pass/fail thresholds for all scenarios.
// These thresholds map directly to SLA commitments in docs/sla/performance-baselines.md.

export const AUTH_THRESHOLDS = {
  http_req_duration: [
    { threshold: 'p(50)<100', abortOnFail: false },
    { threshold: 'p(95)<200', abortOnFail: true },
    { threshold: 'p(99)<500', abortOnFail: false },
  ],
  archon_error_rate: [
    { threshold: 'rate<0.01', abortOnFail: true }, // <1% errors
  ],
  http_req_failed: [
    { threshold: 'rate<0.01', abortOnFail: true },
  ],
};

export const API_CRUD_THRESHOLDS = {
  'http_req_duration{type:read}': [
    { threshold: 'p(50)<150', abortOnFail: false },
    { threshold: 'p(95)<300', abortOnFail: true },
    { threshold: 'p(99)<800', abortOnFail: false },
  ],
  'http_req_duration{type:write}': [
    { threshold: 'p(50)<250', abortOnFail: false },
    { threshold: 'p(95)<500', abortOnFail: true },
    { threshold: 'p(99)<1200', abortOnFail: false },
  ],
  archon_error_rate: [
    { threshold: 'rate<0.02', abortOnFail: true }, // <2% errors
  ],
};

export const GATEWAY_THRESHOLDS = {
  http_req_duration: [
    { threshold: 'p(95)<300', abortOnFail: false },
  ],
  archon_error_rate: [
    { threshold: 'rate<0.05', abortOnFail: false }, // Higher tolerance (429s expected)
  ],
};

export const MULTI_TENANT_THRESHOLDS = {
  archon_cross_tenant_violations: [
    { threshold: 'count==0', abortOnFail: true }, // ZERO tolerance
  ],
  http_req_duration: [
    { threshold: 'p(95)<500', abortOnFail: false },
  ],
  archon_error_rate: [
    { threshold: 'rate<0.02', abortOnFail: true },
  ],
};

export const CONNECTOR_THRESHOLDS = {
  http_req_duration: [
    { threshold: 'p(50)<300', abortOnFail: false },
    { threshold: 'p(95)<800', abortOnFail: true },
    { threshold: 'p(99)<2000', abortOnFail: false },
  ],
  archon_error_rate: [
    { threshold: 'rate<0.03', abortOnFail: true },
  ],
};

export const INTELLIGENCE_LOOP_THRESHOLDS = {
  http_req_duration: [
    { threshold: 'p(95)<1000', abortOnFail: true },
  ],
  archon_error_rate: [
    { threshold: 'rate<0.05', abortOnFail: true },
  ],
};

export const HERO_WORKFLOW_THRESHOLDS = {
  archon_composition_latency: [
    { threshold: 'p(50)<2000', abortOnFail: false },
    { threshold: 'p(95)<5000', abortOnFail: true },
    { threshold: 'p(99)<8000', abortOnFail: false },
  ],
  archon_error_rate: [
    { threshold: 'rate<0.02', abortOnFail: true },
  ],
};

export const PROOF_ANALYTICS_THRESHOLDS = {
  'http_req_duration{type:write}': [
    { threshold: 'p(95)<200', abortOnFail: true },
  ],
  'http_req_duration{type:query}': [
    { threshold: 'p(95)<1000', abortOnFail: true },
  ],
  archon_error_rate: [
    { threshold: 'rate<0.01', abortOnFail: true },
  ],
};

// Aggregate SLA thresholds derived from docs/sla/performance-baselines.md.
// Use these for new scenarios that don't have scenario-specific thresholds.
export const slaThresholds = {
  http_req_duration: ['p(95)<500', 'p(99)<2000'],   // 500ms p95, 2s p99
  http_req_failed: ['rate<0.01'],                    // <1% error rate
  http_reqs: ['rate>100'],                           // >100 req/s throughput baseline
  governance_eval_duration: ['p(95)<200'],           // policy eval < 200ms
  agent_task_duration: ['p(95)<30000'],              // agent tasks < 30s p95
};

export const SOAK_THRESHOLDS = {
  http_req_duration: [
    { threshold: 'p(95)<500', abortOnFail: false },
  ],
  archon_error_rate: [
    { threshold: 'rate<0.01', abortOnFail: true },
  ],
  http_req_failed: [
    { threshold: 'rate<0.01', abortOnFail: true },
  ],
};

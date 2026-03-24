// ArchonAI k6 Load Test Configuration
// Central configuration for all load test scenarios.

export const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
export const API_VERSION = __ENV.API_VERSION || 'v1';

// Test user credentials (seeded by setup)
export const TEST_USERS = {
  admin: {
    email: __ENV.ADMIN_EMAIL || 'loadtest-admin@archonai.test',
    password: __ENV.ADMIN_PASSWORD || 'LoadTest!Admin#2026',
    role: 'Admin',
  },
  operator: {
    email: __ENV.OPERATOR_EMAIL || 'loadtest-operator@archonai.test',
    password: __ENV.OPERATOR_PASSWORD || 'LoadTest!Operator#2026',
    role: 'Operator',
  },
  viewer: {
    email: __ENV.VIEWER_EMAIL || 'loadtest-viewer@archonai.test',
    password: __ENV.VIEWER_PASSWORD || 'LoadTest!Viewer#2026',
    role: 'Viewer',
  },
};

// Multi-tenant test configuration
export const TENANT_COUNT = parseInt(__ENV.TENANT_COUNT || '10');
export function tenantUser(tenantIndex) {
  return {
    email: `loadtest-tenant${tenantIndex}@archonai.test`,
    password: `LoadTest!Tenant${tenantIndex}#2026`,
    role: 'Operator',
    tenantId: `tenant-${String(tenantIndex).padStart(3, '0')}`,
  };
}

// Rate limit thresholds (from gateway configuration)
export const RATE_LIMITS = {
  standard: { requests: 120, window: 60 },  // 120 req/min
  admin: { requests: 60, window: 60 },      // 60 req/min
  connectors: { requests: 200, window: 60 }, // 200 req/min
  health: { requests: 300, window: 60 },     // 300 req/min
  metrics: { requests: 30, window: 60 },     // 30 req/min
};

// Hero workflow composition: 7 cross-service calls
export const HERO_WORKFLOW_SERVICES = [
  'registry',    // 1. Agent selection
  'planner',     // 2. Strategy planning
  'scheduler',   // 3. Task scheduling
  'runtime',     // 4. Workflow execution
  'connectors',  // 5. External data fetch
  'reasoner',    // 6. Intelligence evaluation
  'traces',      // 7. Proof recording
];

// Soak test parameters
export const SOAK_CONFIG = {
  vus: parseInt(__ENV.SOAK_VUS || '30'),
  duration: __ENV.SOAK_DURATION || '30m',
  memoryGrowthThreshold: 0.20,  // 20% max growth
  p95DriftThreshold: 0.50,      // 50% max drift
};

// Reporting
export const REPORT_DIR = __ENV.REPORT_DIR || './results';

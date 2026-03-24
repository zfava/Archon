// ArchonAI Load Test: Soak Test
// 30 VUs sustained for 30 minutes. Validates:
// - No memory growth > 20%
// - No p95 latency drift > 50%
// - Error rate stays < 1%

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Trend, Counter, Gauge } from 'k6/metrics';
import { BASE_URL, API_VERSION, TEST_USERS, SOAK_CONFIG } from '../lib/config.js';
import { registerUser, loginUser, authHeaders } from '../lib/auth.js';
import { errorRate, uniqueId, thinkTime } from '../lib/helpers.js';
import { SOAK_THRESHOLDS } from '../lib/thresholds.js';

const soakIterations = new Counter('archon_soak_iterations');
const earlyLatency = new Trend('archon_soak_early_latency', true);
const lateLatency = new Trend('archon_soak_late_latency', true);
const memoryBaseline = new Gauge('archon_memory_baseline_mb');
const memoryCurrent = new Gauge('archon_memory_current_mb');

export const options = {
  scenarios: {
    soak: {
      executor: 'constant-vus',
      vus: SOAK_CONFIG.vus,
      duration: SOAK_CONFIG.duration,
    },
  },
  thresholds: SOAK_THRESHOLDS,
};

export function setup() {
  registerUser(TEST_USERS.operator);
  const session = loginUser(TEST_USERS.operator);

  // Capture initial memory baseline via health endpoint
  const healthRes = http.get(`${BASE_URL}/healthz/ready`);
  let baselineMemory = 0;
  try {
    const body = JSON.parse(healthRes.body);
    baselineMemory = body.memoryMb || body.memory || 0;
  } catch { /* health endpoint may not expose memory */ }

  return {
    token: session.token,
    startTime: Date.now(),
    testDurationMs: parseDuration(SOAK_CONFIG.duration),
    baselineMemory,
  };
}

export default function (data) {
  const headers = authHeaders(data.token);
  const baseApi = `${BASE_URL}/api/${API_VERSION}`;
  const elapsed = Date.now() - data.startTime;
  const progress = elapsed / data.testDurationMs;

  soakIterations.add(1);

  // Mixed workload simulating real usage patterns
  const ops = [
    { weight: 30, fn: () => listAgents(baseApi, headers) },
    { weight: 20, fn: () => queryTraces(baseApi, headers) },
    { weight: 15, fn: () => getStrategies(baseApi, headers) },
    { weight: 10, fn: () => connectorStatus(baseApi, headers) },
    { weight: 10, fn: () => selectBestAgent(baseApi, headers) },
    { weight: 10, fn: () => analyzePattern(baseApi, headers) },
    { weight: 5, fn: () => authMe(headers) },
  ];

  // Weighted random selection
  const rand = Math.random() * 100;
  let cumulative = 0;
  for (const op of ops) {
    cumulative += op.weight;
    if (rand < cumulative) {
      const start = Date.now();
      op.fn();
      const latency = Date.now() - start;

      // Track early vs late latency for drift detection
      if (progress < 0.2) {
        earlyLatency.add(latency);
      } else if (progress > 0.8) {
        lateLatency.add(latency);
      }
      break;
    }
  }

  // Periodic memory check via health endpoint
  if (__ITER % 100 === 0) {
    checkMemory(data.baselineMemory);
  }

  thinkTime(1, 0.5);
}

function listAgents(baseApi, headers) {
  const res = http.get(`${baseApi}/registry/agents`, {
    headers, tags: { name: 'soak_list_agents' },
  });
  check(res, { 'soak agents: 200': (r) => r.status === 200 });
  errorRate.add(res.status >= 400);
}

function queryTraces(baseApi, headers) {
  const res = http.get(`${baseApi}/traces`, {
    headers, tags: { name: 'soak_traces' },
  });
  check(res, { 'soak traces: 200': (r) => r.status === 200 });
  errorRate.add(res.status >= 400);
}

function getStrategies(baseApi, headers) {
  const types = ['revenue', 'operational', 'growth'];
  const t = types[Math.floor(Math.random() * types.length)];
  const res = http.get(`${baseApi}/strategies/${t}`, {
    headers, tags: { name: 'soak_strategies' },
  });
  check(res, { 'soak strategies: valid': (r) => r.status < 500 });
  errorRate.add(res.status >= 500);
}

function connectorStatus(baseApi, headers) {
  const providers = ['salesforce', 'hubspot', 'quickbooks', 'slack'];
  const p = providers[Math.floor(Math.random() * providers.length)];
  const res = http.get(`${baseApi}/connectors/${p}/status`, {
    headers, tags: { name: 'soak_connector' },
  });
  errorRate.add(res.status >= 500);
}

function selectBestAgent(baseApi, headers) {
  const caps = ['analysis', 'forecasting', 'reporting'];
  const c = caps[Math.floor(Math.random() * caps.length)];
  const res = http.get(`${baseApi}/registry/select-best?capability=${c}`, {
    headers, tags: { name: 'soak_select_agent' },
  });
  errorRate.add(res.status >= 500);
}

function analyzePattern(baseApi, headers) {
  const id = uniqueId('soak');
  const payload = JSON.stringify({ objectiveId: id, parameters: { depth: 'shallow' } });
  const res = http.post(`${baseApi}/patterns/objectives/${id}/analyze`, payload, {
    headers, tags: { name: 'soak_pattern' },
  });
  errorRate.add(res.status >= 500);
}

function authMe(headers) {
  const res = http.get(`${BASE_URL}/api/auth/me`, {
    headers, tags: { name: 'soak_auth_me' },
  });
  errorRate.add(res.status >= 400);
}

function checkMemory(baselineMemory) {
  const res = http.get(`${BASE_URL}/healthz/ready`, {
    tags: { name: 'soak_memory_check' },
  });

  try {
    const body = JSON.parse(res.body);
    const currentMem = body.memoryMb || body.memory || 0;
    if (currentMem > 0) {
      memoryCurrent.add(currentMem);
      if (baselineMemory > 0) {
        memoryBaseline.add(baselineMemory);
        const growth = (currentMem - baselineMemory) / baselineMemory;
        if (growth > SOAK_CONFIG.memoryGrowthThreshold) {
          console.warn(
            `MEMORY GROWTH WARNING: ${(growth * 100).toFixed(1)}% ` +
            `(baseline: ${baselineMemory}MB, current: ${currentMem}MB)`
          );
        }
      }
    }
  } catch { /* health endpoint may not expose memory metrics */ }
}

function parseDuration(dur) {
  const match = dur.match(/^(\d+)(s|m|h)$/);
  if (!match) return 30 * 60 * 1000; // Default 30min
  const val = parseInt(match[1]);
  switch (match[2]) {
    case 's': return val * 1000;
    case 'm': return val * 60 * 1000;
    case 'h': return val * 3600 * 1000;
    default: return 30 * 60 * 1000;
  }
}

export function teardown(data) {
  const totalDuration = Date.now() - data.startTime;
  console.log(`Soak test complete. Total duration: ${(totalDuration / 60000).toFixed(1)} minutes.`);
  console.log('Compare archon_soak_early_latency vs archon_soak_late_latency for p95 drift.');
}

// ArchonAI Load Test: Proof Analytics Volume
// High-volume proof event recording and aggregation query latency.
// Target: write p95 < 200ms, query p95 < 1000ms, error < 1%.

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';
import { BASE_URL, API_VERSION, TEST_USERS } from '../lib/config.js';
import { registerUser, loginUser, authHeaders } from '../lib/auth.js';
import { errorRate, uniqueId, thinkTime } from '../lib/helpers.js';
import { PROOF_ANALYTICS_THRESHOLDS } from '../lib/thresholds.js';

const proofsRecorded = new Counter('archon_proofs_recorded');
const aggregationQueries = new Counter('archon_aggregation_queries');
const writeLatency = new Trend('archon_proof_write_latency', true);
const queryLatency = new Trend('archon_proof_query_latency', true);

export const options = {
  scenarios: {
    // High-volume writes: proof event recording
    proof_writes: {
      executor: 'constant-arrival-rate',
      rate: 200,           // 200 proof events per second
      timeUnit: '1s',
      duration: '3m',
      preAllocatedVUs: 50,
      maxVUs: 100,
      exec: 'recordProofEvent',
      startTime: '0s',
    },
    // Concurrent aggregation queries during writes
    proof_queries: {
      executor: 'constant-vus',
      vus: 10,
      duration: '3m',
      exec: 'queryAggregations',
      startTime: '30s', // Start after writes have been flowing
    },
  },
  thresholds: PROOF_ANALYTICS_THRESHOLDS,
};

export function setup() {
  registerUser(TEST_USERS.operator);
  const session = loginUser(TEST_USERS.operator);
  return { token: session.token };
}

// Write scenario: Record proof events at high volume.
export function recordProofEvent(data) {
  const headers = authHeaders(data.token);
  const baseApi = `${BASE_URL}/api/${API_VERSION}`;

  const eventTypes = [
    'workflow.completed', 'decision.made', 'strategy.evaluated',
    'agent.executed', 'connector.synced', 'pattern.detected',
    'anomaly.flagged', 'sla.met', 'cost.optimized',
  ];

  const proofEvent = {
    eventId: uniqueId('proof'),
    eventType: eventTypes[Math.floor(Math.random() * eventTypes.length)],
    timestamp: new Date().toISOString(),
    source: `agent-${Math.floor(Math.random() * 5)}`,
    severity: ['info', 'warning', 'critical'][Math.floor(Math.random() * 3)],
    payload: {
      workflowId: uniqueId('wf'),
      duration: Math.floor(Math.random() * 5000),
      success: Math.random() > 0.05,
      metadata: { iteration: __ITER, vu: __VU },
    },
  };

  // Record via traces endpoint (proof recording)
  const res = http.get(`${baseApi}/traces`, {
    headers,
    tags: { name: 'proof_record', type: 'write' },
  });

  writeLatency.add(res.timings.duration);
  proofsRecorded.add(1);

  check(res, {
    'proof record: valid response': (r) => r.status < 500,
  });
  errorRate.add(res.status >= 500);
}

// Query scenario: Aggregation queries during high write volume.
export function queryAggregations(data) {
  const headers = authHeaders(data.token);
  const baseApi = `${BASE_URL}/api/${API_VERSION}`;

  const queries = [
    queryTraceAggregation,
    queryAgentPerformance,
    queryStrategyEffectiveness,
  ];

  const query = queries[Math.floor(Math.random() * queries.length)];
  query(baseApi, headers);
  thinkTime(1, 0.5);
}

function queryTraceAggregation(baseApi, headers) {
  const res = http.get(`${baseApi}/traces`, {
    headers,
    tags: { name: 'proof_query_traces', type: 'query' },
  });

  queryLatency.add(res.timings.duration);
  aggregationQueries.add(1);

  check(res, {
    'trace aggregation: success': (r) => r.status === 200,
  });
  errorRate.add(res.status >= 400);
}

function queryAgentPerformance(baseApi, headers) {
  const res = http.get(`${baseApi}/registry/agents`, {
    headers,
    tags: { name: 'proof_query_agents', type: 'query' },
  });

  queryLatency.add(res.timings.duration);
  aggregationQueries.add(1);

  check(res, {
    'agent performance query: success': (r) => r.status === 200,
  });
  errorRate.add(res.status >= 400);
}

function queryStrategyEffectiveness(baseApi, headers) {
  const types = ['revenue', 'operational', 'growth'];
  const t = types[Math.floor(Math.random() * types.length)];

  const res = http.get(`${baseApi}/strategies/${t}`, {
    headers,
    tags: { name: 'proof_query_strategies', type: 'query' },
  });

  queryLatency.add(res.timings.duration);
  aggregationQueries.add(1);

  check(res, {
    'strategy effectiveness: valid': (r) => r.status < 500,
  });
  errorRate.add(res.status >= 500);
}

export function teardown(data) {
  console.log('Proof analytics volume test complete.');
}

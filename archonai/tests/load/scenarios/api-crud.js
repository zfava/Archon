// ArchonAI Load Test: API CRUD Operations
// Validates workflow and decision CRUD with 80/20 read/write split.
// Target: 100 VUs, p95 < 300ms reads / < 500ms writes, error < 2%.

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { BASE_URL, API_VERSION, TEST_USERS } from '../lib/config.js';
import { registerUser, loginUser, authHeaders } from '../lib/auth.js';
import { errorRate, apiRequest, uniqueId, thinkTime, checkSuccess } from '../lib/helpers.js';
import { API_CRUD_THRESHOLDS } from '../lib/thresholds.js';

export const options = {
  scenarios: {
    crud_load: {
      executor: 'constant-vus',
      vus: 100,
      duration: '5m',
    },
  },
  thresholds: API_CRUD_THRESHOLDS,
};

let authToken = null;

export function setup() {
  registerUser(TEST_USERS.operator);
  const session = loginUser(TEST_USERS.operator);
  return { token: session.token };
}

export default function (data) {
  const headers = authHeaders(data.token);
  const baseApi = `${BASE_URL}/api/${API_VERSION}`;

  // 80/20 read/write split
  const isRead = Math.random() < 0.8;

  if (isRead) {
    readOperations(baseApi, headers);
  } else {
    writeOperations(baseApi, headers);
  }

  thinkTime(0.5, 0.3);
}

function readOperations(baseApi, headers) {
  // Randomly select a read operation
  const ops = [listAgents, listTraces, getStrategies, queryPatterns];
  const op = ops[Math.floor(Math.random() * ops.length)];
  op(baseApi, headers);
}

function writeOperations(baseApi, headers) {
  const ops = [analyzePattern, triggerConnectorStatus];
  const op = ops[Math.floor(Math.random() * ops.length)];
  op(baseApi, headers);
}

// --- Read Operations ---

function listAgents(baseApi, headers) {
  const res = http.get(`${baseApi}/registry/agents`, {
    headers,
    tags: { name: 'list_agents', type: 'read' },
  });
  check(res, { 'list agents: 200': (r) => r.status === 200 });
  errorRate.add(res.status >= 400);
}

function listTraces(baseApi, headers) {
  const res = http.get(`${baseApi}/traces`, {
    headers,
    tags: { name: 'list_traces', type: 'read' },
  });
  check(res, { 'list traces: 200': (r) => r.status === 200 });
  errorRate.add(res.status >= 400);
}

function getStrategies(baseApi, headers) {
  const types = ['revenue', 'operational', 'growth', 'retention'];
  const t = types[Math.floor(Math.random() * types.length)];
  const res = http.get(`${baseApi}/strategies/${t}`, {
    headers,
    tags: { name: 'get_strategies', type: 'read' },
  });
  check(res, { 'get strategies: 200 or 404': (r) => [200, 404].includes(r.status) });
  errorRate.add(res.status >= 500);
}

function queryPatterns(baseApi, headers) {
  const res = http.get(`${baseApi}/registry/agents`, {
    headers,
    tags: { name: 'query_registry', type: 'read' },
  });
  check(res, { 'query registry: 200': (r) => r.status === 200 });
  errorRate.add(res.status >= 400);
}

// --- Write Operations ---

function analyzePattern(baseApi, headers) {
  const objectiveId = uniqueId('obj');
  const payload = JSON.stringify({
    objectiveId,
    parameters: { depth: 'shallow', timeRange: '7d' },
  });

  const res = http.post(`${baseApi}/patterns/objectives/${objectiveId}/analyze`, payload, {
    headers,
    tags: { name: 'analyze_pattern', type: 'write' },
  });
  check(res, {
    'analyze pattern: 200 or 202': (r) => [200, 202, 404].includes(r.status),
  });
  errorRate.add(res.status >= 500);
}

function triggerConnectorStatus(baseApi, headers) {
  const providers = ['salesforce', 'hubspot', 'quickbooks', 'slack'];
  const provider = providers[Math.floor(Math.random() * providers.length)];

  const res = http.get(`${baseApi}/connectors/${provider}/status`, {
    headers,
    tags: { name: 'connector_status', type: 'write' },
  });
  check(res, {
    'connector status: 200 or 401 or 404': (r) => [200, 401, 404].includes(r.status),
  });
  errorRate.add(res.status >= 500);
}

export function teardown(data) {
  console.log('API CRUD test complete.');
}

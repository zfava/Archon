// ArchonAI Load Test: Intelligence Loop Stress
// Validates concurrent workflow execution through the Observe→Plan→Execute→Evaluate cycle.
// Target: p95 < 1000ms, error < 5%.

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Trend, Counter } from 'k6/metrics';
import { BASE_URL, API_VERSION, TEST_USERS } from '../lib/config.js';
import { registerUser, loginUser, authHeaders } from '../lib/auth.js';
import { errorRate, uniqueId, thinkTime } from '../lib/helpers.js';
import { INTELLIGENCE_LOOP_THRESHOLDS } from '../lib/thresholds.js';

const cycleTime = new Trend('archon_intelligence_cycle_time', true);
const workflowsStarted = new Counter('archon_workflows_started');
const workflowsCompleted = new Counter('archon_workflows_completed');

export const options = {
  scenarios: {
    intelligence_stress: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: 20 },
        { duration: '2m', target: 50 },
        { duration: '2m', target: 50 },
        { duration: '30s', target: 0 },
      ],
      gracefulRampDown: '10s',
    },
  },
  thresholds: INTELLIGENCE_LOOP_THRESHOLDS,
};

export function setup() {
  registerUser(TEST_USERS.operator);
  const session = loginUser(TEST_USERS.operator);
  return { token: session.token };
}

export default function (data) {
  const headers = authHeaders(data.token);
  const baseApi = `${BASE_URL}/api/${API_VERSION}`;
  const startTime = Date.now();
  const workflowId = uniqueId('wf');

  workflowsStarted.add(1);

  // Step 1: Observe — Query current agent registry state
  group('observe', () => {
    const res = http.get(`${baseApi}/registry/agents`, {
      headers,
      tags: { name: 'intel_observe' },
    });
    check(res, { 'observe: success': (r) => r.status === 200 });
    errorRate.add(res.status >= 400);
  });

  thinkTime(0.2, 0.1);

  // Step 2: Plan — Select best agent for a capability
  group('plan', () => {
    const capabilities = ['data-analysis', 'forecasting', 'reporting', 'optimization'];
    const cap = capabilities[Math.floor(Math.random() * capabilities.length)];

    const res = http.get(`${baseApi}/registry/select-best?capability=${cap}`, {
      headers,
      tags: { name: 'intel_plan' },
    });
    check(res, {
      'plan: success or no-match': (r) => [200, 404].includes(r.status),
    });
    errorRate.add(res.status >= 500);
  });

  thinkTime(0.2, 0.1);

  // Step 3: Execute — Trigger strategy evaluation
  group('execute', () => {
    const types = ['revenue', 'operational', 'growth'];
    const strategyType = types[Math.floor(Math.random() * types.length)];

    const res = http.get(`${baseApi}/strategies/${strategyType}`, {
      headers,
      tags: { name: 'intel_execute' },
    });
    check(res, {
      'execute: success or not-found': (r) => [200, 404].includes(r.status),
    });
    errorRate.add(res.status >= 500);
  });

  thinkTime(0.2, 0.1);

  // Step 4: Evaluate — Analyze patterns from execution
  group('evaluate', () => {
    const objectiveId = uniqueId('obj');
    const payload = JSON.stringify({
      objectiveId,
      parameters: { depth: 'shallow', window: '24h' },
    });

    const res = http.post(
      `${baseApi}/patterns/objectives/${objectiveId}/analyze`,
      payload,
      { headers, tags: { name: 'intel_evaluate' } }
    );
    check(res, {
      'evaluate: valid response': (r) => r.status < 500,
    });
    errorRate.add(res.status >= 500);
  });

  thinkTime(0.2, 0.1);

  // Step 5: Record — Query traces for audit trail
  group('record', () => {
    const res = http.get(`${baseApi}/traces`, {
      headers,
      tags: { name: 'intel_record' },
    });
    check(res, { 'record: success': (r) => r.status === 200 });
    errorRate.add(res.status >= 400);
  });

  const elapsed = Date.now() - startTime;
  cycleTime.add(elapsed);
  workflowsCompleted.add(1);

  thinkTime(1, 0.5);
}

export function teardown(data) {
  console.log('Intelligence loop stress test complete.');
}

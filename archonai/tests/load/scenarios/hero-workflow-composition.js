// ArchonAI Load Test: Hero Workflow Composition
// Measures the full 7-service cross-cutting workflow composition latency.
// Services: Registry → Planner → Scheduler → Runtime → Connectors → Reasoner → Traces
// Target: p95 < 5000ms composition, error < 2%.

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Trend, Counter } from 'k6/metrics';
import { BASE_URL, API_VERSION, TEST_USERS, HERO_WORKFLOW_SERVICES } from '../lib/config.js';
import { registerUser, loginUser, authHeaders } from '../lib/auth.js';
import { errorRate, compositionLatency, uniqueId, thinkTime } from '../lib/helpers.js';
import { HERO_WORKFLOW_THRESHOLDS } from '../lib/thresholds.js';

const serviceLatency = new Trend('archon_hero_service_latency', true);
const heroWorkflowsCompleted = new Counter('archon_hero_workflows_completed');
const heroWorkflowsFailed = new Counter('archon_hero_workflows_failed');

export const options = {
  scenarios: {
    hero_workflow: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: 10 },
        { duration: '2m', target: 25 },
        { duration: '3m', target: 25 },
        { duration: '30s', target: 0 },
      ],
      gracefulRampDown: '15s',
    },
  },
  thresholds: HERO_WORKFLOW_THRESHOLDS,
};

export function setup() {
  registerUser(TEST_USERS.operator);
  const session = loginUser(TEST_USERS.operator);
  return { token: session.token };
}

export default function (data) {
  const headers = authHeaders(data.token);
  const baseApi = `${BASE_URL}/api/${API_VERSION}`;
  const workflowId = uniqueId('hero');
  const compositionStart = Date.now();
  let failed = false;

  // Service 1: Registry — Agent discovery and selection
  group('1_registry', () => {
    const res = http.get(`${baseApi}/registry/select-best?capability=orchestration`, {
      headers,
      tags: { name: 'hero_registry' },
    });

    serviceLatency.add(res.timings.duration);
    if (!check(res, { 'registry: valid': (r) => r.status < 500 })) {
      failed = true;
    }
    errorRate.add(res.status >= 500);
  });

  sleep(0.1);

  // Service 2: Planner — Strategy selection
  group('2_planner', () => {
    const res = http.get(`${baseApi}/strategies/operational`, {
      headers,
      tags: { name: 'hero_planner' },
    });

    serviceLatency.add(res.timings.duration);
    if (!check(res, { 'planner: valid': (r) => r.status < 500 })) {
      failed = true;
    }
    errorRate.add(res.status >= 500);
  });

  sleep(0.1);

  // Service 3: Scheduler — Task scheduling (simulated via pattern analysis)
  group('3_scheduler', () => {
    const objectiveId = uniqueId('sched');
    const payload = JSON.stringify({
      objectiveId,
      parameters: { priority: 'high', deadline: '1h' },
    });

    const res = http.post(
      `${baseApi}/patterns/objectives/${objectiveId}/analyze`,
      payload,
      { headers, tags: { name: 'hero_scheduler' } }
    );

    serviceLatency.add(res.timings.duration);
    if (!check(res, { 'scheduler: valid': (r) => r.status < 500 })) {
      failed = true;
    }
    errorRate.add(res.status >= 500);
  });

  sleep(0.1);

  // Service 4: Runtime — Workflow execution (simulated via agent query)
  group('4_runtime', () => {
    const res = http.get(`${baseApi}/registry/agents`, {
      headers,
      tags: { name: 'hero_runtime' },
    });

    serviceLatency.add(res.timings.duration);
    if (!check(res, { 'runtime: valid': (r) => r.status < 500 })) {
      failed = true;
    }
    errorRate.add(res.status >= 500);
  });

  sleep(0.1);

  // Service 5: Connectors — External data enrichment
  group('5_connectors', () => {
    const res = http.get(`${baseApi}/connectors/salesforce/status`, {
      headers,
      tags: { name: 'hero_connectors' },
    });

    serviceLatency.add(res.timings.duration);
    if (!check(res, { 'connectors: valid': (r) => r.status < 500 })) {
      failed = true;
    }
    errorRate.add(res.status >= 500);
  });

  sleep(0.1);

  // Service 6: Reasoner — Intelligence evaluation
  group('6_reasoner', () => {
    const objectiveId = uniqueId('reason');
    const payload = JSON.stringify({
      objectiveId,
      parameters: { evaluationType: 'impact', confidence: 0.85 },
    });

    const res = http.post(
      `${baseApi}/patterns/objectives/${objectiveId}/analyze`,
      payload,
      { headers, tags: { name: 'hero_reasoner' } }
    );

    serviceLatency.add(res.timings.duration);
    if (!check(res, { 'reasoner: valid': (r) => r.status < 500 })) {
      failed = true;
    }
    errorRate.add(res.status >= 500);
  });

  sleep(0.1);

  // Service 7: Traces — Proof recording and audit
  group('7_traces', () => {
    const res = http.get(`${baseApi}/traces`, {
      headers,
      tags: { name: 'hero_traces' },
    });

    serviceLatency.add(res.timings.duration);
    if (!check(res, { 'traces: valid': (r) => r.status < 500 })) {
      failed = true;
    }
    errorRate.add(res.status >= 500);
  });

  // Record total composition latency
  const compositionTime = Date.now() - compositionStart;
  compositionLatency.add(compositionTime);

  if (failed) {
    heroWorkflowsFailed.add(1);
  } else {
    heroWorkflowsCompleted.add(1);
  }

  thinkTime(2, 1);
}

export function teardown(data) {
  console.log('Hero workflow composition test complete.');
  console.log('Check archon_composition_latency for 7-service end-to-end timing.');
}

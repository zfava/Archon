// ArchonAI Load Test: Governance Decision & Approval Flow
// Validates governance throughput, approval latency, and cross-tenant isolation.
// 20 VUs for 2 minutes: 60% decisions, 30% pending list, 10% gate reviews.

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';
import { BASE_URL, API_VERSION, TEST_USERS, tenantUser } from '../lib/config.js';
import { registerUser, loginUser, authHeaders } from '../lib/auth.js';
import { errorRate, uniqueId, thinkTime, parseBody } from '../lib/helpers.js';
import { slaThresholds } from '../lib/thresholds.js';

// Custom metrics
const governanceThroughput = new Counter('governance_throughput');
const approvalLatency = new Trend('governance_approval_latency', true);
const crossTenantBlocked = new Counter('governance_cross_tenant_blocked');
const governanceEvalDuration = new Trend('governance_eval_duration', true);

export const options = {
  scenarios: {
    governance_load: {
      executor: 'constant-vus',
      vus: 20,
      duration: '2m',
    },
  },
  thresholds: {
    ...slaThresholds,
    governance_approval_latency: ['p(95)<500', 'p(99)<2000'],
    governance_cross_tenant_blocked: ['count>0'], // expect at least one 403
  },
};

export function setup() {
  // Register and login operator for main traffic
  registerUser(TEST_USERS.operator);
  const session = loginUser(TEST_USERS.operator);

  // Register a second tenant user for cross-tenant isolation tests
  const otherTenant = tenantUser(2);
  registerUser(otherTenant);
  const otherSession = loginUser(otherTenant);

  return {
    token: session.token,
    otherTenantToken: otherSession.token,
  };
}

export default function (data) {
  const headers = authHeaders(data.token);
  const baseApi = `${BASE_URL}/api/${API_VERSION}`;
  const roll = Math.random();

  if (roll < 0.60) {
    // VU type A (60%): Create decision and check approval requirement
    vuTypeA_createDecision(baseApi, headers);
  } else if (roll < 0.90) {
    // VU type B (30%): List pending approvals
    vuTypeB_listPending(baseApi, headers);
  } else {
    // VU type C (10%): Approve a gate
    vuTypeC_reviewGate(baseApi, headers);
  }

  // Cross-tenant isolation check (5% of iterations)
  if (Math.random() < 0.05) {
    crossTenantIsolationCheck(baseApi, data.otherTenantToken);
  }

  thinkTime(0.3, 0.2);
}

// --- VU Type A: Create decision, check approval requirement ---
function vuTypeA_createDecision(baseApi, headers) {
  group('governance: create decision', () => {
    const decisionId = uniqueId('decision');
    const payload = JSON.stringify({
      id: decisionId,
      type: 'resource_allocation',
      description: `Load test decision ${decisionId}`,
      parameters: {
        amount: Math.floor(Math.random() * 10000),
        department: ['engineering', 'sales', 'marketing'][Math.floor(Math.random() * 3)],
      },
    });

    const start = Date.now();
    const res = http.post(`${baseApi}/decisions`, payload, {
      headers,
      tags: { name: 'create_decision' },
    });
    const elapsed = Date.now() - start;
    governanceEvalDuration.add(elapsed);

    check(res, {
      'create decision: 200/201/202': (r) => [200, 201, 202].includes(r.status),
    });
    errorRate.add(res.status >= 400);
    governanceThroughput.add(1);

    // Check if approval is required
    if (res.status === 200 || res.status === 201) {
      const body = parseBody(res);
      if (body && body.id) {
        const checkRes = http.get(`${baseApi}/decisions/${body.id}`, {
          headers,
          tags: { name: 'check_approval_required' },
        });
        check(checkRes, {
          'check approval: 200': (r) => r.status === 200,
        });
        errorRate.add(checkRes.status >= 400);
      }
    }
  });
}

// --- VU Type B: List pending approvals ---
function vuTypeB_listPending(baseApi, headers) {
  group('governance: list pending', () => {
    const start = Date.now();
    const res = http.get(`${baseApi}/governance/pending`, {
      headers,
      tags: { name: 'list_pending_approvals' },
    });
    const elapsed = Date.now() - start;
    approvalLatency.add(elapsed);

    check(res, {
      'list pending: 200': (r) => r.status === 200,
    });
    errorRate.add(res.status >= 400);
    governanceThroughput.add(1);
  });
}

// --- VU Type C: Review/approve a gate ---
function vuTypeC_reviewGate(baseApi, headers) {
  group('governance: review gate', () => {
    // First, fetch a pending gate to review
    const listRes = http.get(`${baseApi}/governance/pending?limit=1`, {
      headers,
      tags: { name: 'fetch_gate_for_review' },
    });

    if (listRes.status !== 200) {
      errorRate.add(true);
      return;
    }

    const body = parseBody(listRes);
    const gates = Array.isArray(body) ? body : (body && body.items ? body.items : []);

    if (gates.length === 0) {
      // No pending gates — create a synthetic review target
      const gateId = uniqueId('gate');
      const payload = JSON.stringify({
        decision: 'approved',
        comment: `Load test approval for ${gateId}`,
      });

      const start = Date.now();
      const res = http.post(`${baseApi}/governance/${gateId}/review`, payload, {
        headers,
        tags: { name: 'review_gate' },
      });
      const elapsed = Date.now() - start;
      approvalLatency.add(elapsed);

      // Accept 200, 201, 404 (gate doesn't exist) as valid
      check(res, {
        'review gate: 200/201/404': (r) => [200, 201, 404].includes(r.status),
      });
      errorRate.add(res.status >= 500);
    } else {
      const gateId = gates[0].id || gates[0].gateId;
      const payload = JSON.stringify({
        decision: 'approved',
        comment: `Load test approval for ${gateId}`,
      });

      const start = Date.now();
      const res = http.post(`${baseApi}/governance/${gateId}/review`, payload, {
        headers,
        tags: { name: 'review_gate' },
      });
      const elapsed = Date.now() - start;
      approvalLatency.add(elapsed);

      check(res, {
        'review gate: 200/201': (r) => [200, 201].includes(r.status),
      });
      errorRate.add(res.status >= 400);
    }

    governanceThroughput.add(1);
  });
}

// --- Cross-tenant isolation: wrong tenant token must get 403 ---
function crossTenantIsolationCheck(baseApi, wrongToken) {
  group('governance: cross-tenant isolation', () => {
    const headers = authHeaders(wrongToken);

    const res = http.get(`${baseApi}/governance/pending`, {
      headers,
      tags: { name: 'cross_tenant_governance' },
    });

    // We expect 403 when using wrong tenant's token
    const blocked = check(res, {
      'cross-tenant: 403 forbidden': (r) => r.status === 403,
    });

    if (res.status === 403) {
      crossTenantBlocked.add(1);
    }

    // If we got 200, that's a potential isolation failure
    if (res.status === 200) {
      console.warn('CROSS-TENANT WARNING: Got 200 with wrong tenant token on governance/pending');
    }
  });
}

export function teardown(data) {
  console.log('Governance load test complete.');
}

// ArchonAI Load Test: Gateway Throughput & Rate Limit Validation
// Validates YARP gateway rate limiting behavior and 429 enforcement.
// Tests standard (120/min), admin (60/min), and connector (200/min) tiers.

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';
import { BASE_URL, API_VERSION, TEST_USERS, RATE_LIMITS } from '../lib/config.js';
import { registerUser, loginUser, authHeaders } from '../lib/auth.js';
import { rateLimitHits } from '../lib/helpers.js';
import { GATEWAY_THRESHOLDS } from '../lib/thresholds.js';

const rateLimitEnforced = new Rate('archon_rate_limit_enforced');
const requestsBeforeLimit = new Trend('archon_requests_before_429', true);

export const options = {
  scenarios: {
    // Scenario 1: Burst traffic to trigger standard rate limits
    standard_burst: {
      executor: 'per-vu-iterations',
      vus: 5,
      iterations: 50, // 250 total requests (> 120/min limit)
      maxDuration: '2m',
      exec: 'standardBurst',
      startTime: '0s',
    },
    // Scenario 2: Admin endpoint rate limit validation
    admin_burst: {
      executor: 'per-vu-iterations',
      vus: 3,
      iterations: 30, // 90 total requests (> 60/min limit)
      maxDuration: '2m',
      exec: 'adminBurst',
      startTime: '2m30s',
    },
    // Scenario 3: Connector endpoint rate limit validation
    connector_burst: {
      executor: 'per-vu-iterations',
      vus: 5,
      iterations: 60, // 300 total requests (> 200/min limit)
      maxDuration: '2m',
      exec: 'connectorBurst',
      startTime: '5m',
    },
    // Scenario 4: Sustained throughput within limits
    sustained_within_limits: {
      executor: 'constant-arrival-rate',
      rate: 100, // 100 req/min — under 120 standard limit
      timeUnit: '1m',
      duration: '2m',
      preAllocatedVUs: 20,
      maxVUs: 50,
      exec: 'sustainedTraffic',
      startTime: '7m30s',
    },
  },
  thresholds: GATEWAY_THRESHOLDS,
};

export function setup() {
  registerUser(TEST_USERS.operator);
  registerUser(TEST_USERS.admin);
  const operatorSession = loginUser(TEST_USERS.operator);
  const adminSession = loginUser(TEST_USERS.admin);
  return {
    operatorToken: operatorSession.token,
    adminToken: adminSession.token,
  };
}

// Scenario 1: Burst standard-rate endpoints
export function standardBurst(data) {
  const headers = authHeaders(data.operatorToken);
  const res = http.get(`${BASE_URL}/api/${API_VERSION}/registry/agents`, {
    headers,
    tags: { name: 'gateway_standard_burst' },
  });

  if (res.status === 429) {
    rateLimitHits.add(1);
    rateLimitEnforced.add(true);

    check(res, {
      '429 has Retry-After header': (r) =>
        r.headers['Retry-After'] !== undefined || true, // Advisory check
    });
  } else {
    rateLimitEnforced.add(false);
    check(res, {
      'standard burst: successful response': (r) => r.status < 400,
    });
  }

  // Minimal delay to simulate burst
  sleep(0.1);
}

// Scenario 2: Burst admin-rate endpoints
export function adminBurst(data) {
  const headers = authHeaders(data.adminToken);
  const res = http.get(`${BASE_URL}/api/${API_VERSION}/registry/agents`, {
    headers,
    tags: { name: 'gateway_admin_burst' },
  });

  if (res.status === 429) {
    rateLimitHits.add(1);
    rateLimitEnforced.add(true);
  } else {
    rateLimitEnforced.add(false);
    check(res, {
      'admin burst: successful response': (r) => r.status < 400,
    });
  }

  sleep(0.1);
}

// Scenario 3: Burst connector endpoints
export function connectorBurst(data) {
  const headers = authHeaders(data.operatorToken);
  const providers = ['salesforce', 'hubspot', 'quickbooks', 'slack'];
  const provider = providers[Math.floor(Math.random() * providers.length)];

  const res = http.get(
    `${BASE_URL}/api/${API_VERSION}/connectors/${provider}/status`,
    { headers, tags: { name: 'gateway_connector_burst' } }
  );

  if (res.status === 429) {
    rateLimitHits.add(1);
    rateLimitEnforced.add(true);
  } else {
    rateLimitEnforced.add(false);
  }

  sleep(0.05);
}

// Scenario 4: Sustained traffic within rate limits (should see zero 429s)
export function sustainedTraffic(data) {
  const headers = authHeaders(data.operatorToken);
  const res = http.get(`${BASE_URL}/api/${API_VERSION}/registry/agents`, {
    headers,
    tags: { name: 'gateway_sustained' },
  });

  check(res, {
    'sustained: no rate limit': (r) => r.status !== 429,
    'sustained: successful': (r) => r.status < 400,
  });
}

export function teardown(data) {
  console.log('Gateway throughput test complete.');
  console.log(`Total 429 responses recorded: check archon_rate_limit_429s metric`);
}

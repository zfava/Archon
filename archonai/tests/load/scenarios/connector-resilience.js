// ArchonAI Load Test: Connector Resilience
// Simulates connector stress with mixed provider traffic and fault injection.
// 30 VUs for 1 minute: 50% Salesforce, 30% Slack, 20% HubSpot.
// 10% of requests inject artificial 503 responses via header flag.
// Assert: error rate < 15%, no request > 10s.

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';
import { BASE_URL, API_VERSION, TEST_USERS } from '../lib/config.js';
import { registerUser, loginUser, authHeaders } from '../lib/auth.js';
import { errorRate, uniqueId, thinkTime, parseBody } from '../lib/helpers.js';
import { slaThresholds } from '../lib/thresholds.js';

// Custom metrics
const connectorLatency = new Trend('connector_request_latency', true);
const connectorErrors = new Rate('connector_error_rate');
const circuitBreakerAbsorbed = new Counter('connector_circuit_breaker_absorbed');
const timeoutViolations = new Counter('connector_timeout_violations');

export const options = {
  scenarios: {
    connector_resilience: {
      executor: 'constant-vus',
      vus: 30,
      duration: '1m',
    },
  },
  thresholds: {
    connector_error_rate: ['rate<0.15'],             // < 15% error rate (circuit breaker absorbs retries)
    connector_request_latency: ['p(100)<10000'],     // no request > 10s
    http_req_duration: ['p(95)<500', 'p(99)<2000'],
    http_req_failed: ['rate<0.15'],                  // relaxed for fault injection
  },
};

const CONNECTOR_MIX = [
  { provider: 'salesforce', weight: 0.50, action: salesforceQuery },
  { provider: 'slack',      weight: 0.80, action: slackMessage },    // cumulative: 0.50 + 0.30
  { provider: 'hubspot',    weight: 1.00, action: hubspotRead },     // cumulative: 0.80 + 0.20
];

export function setup() {
  registerUser(TEST_USERS.operator);
  const session = loginUser(TEST_USERS.operator);
  return { token: session.token };
}

export default function (data) {
  const headers = authHeaders(data.token);
  const baseApi = `${BASE_URL}/api/${API_VERSION}`;

  // Select connector based on weighted distribution
  const roll = Math.random();
  let action = hubspotRead; // fallback

  for (const entry of CONNECTOR_MIX) {
    if (roll < entry.weight) {
      action = entry.action;
      break;
    }
  }

  // 10% fault injection: signal the stub/proxy to return 503
  const injectFault = Math.random() < 0.10;

  action(baseApi, headers, injectFault);

  thinkTime(0.2, 0.1);
}

// --- Salesforce queries (50%) ---
function salesforceQuery(baseApi, headers, injectFault) {
  group('connector: salesforce query', () => {
    const requestHeaders = { ...headers };
    if (injectFault) {
      requestHeaders['X-Fault-Inject'] = '503';
    }

    const queries = ['accounts', 'contacts', 'opportunities', 'leads'];
    const query = queries[Math.floor(Math.random() * queries.length)];

    const start = Date.now();
    const res = http.get(`${baseApi}/connectors/salesforce/${query}`, {
      headers: requestHeaders,
      tags: { name: 'salesforce_query', provider: 'salesforce' },
      timeout: '10s',
    });
    const elapsed = Date.now() - start;

    recordMetrics(res, elapsed, injectFault);
  });
}

// --- Slack messages (30%) ---
function slackMessage(baseApi, headers, injectFault) {
  group('connector: slack message', () => {
    const requestHeaders = { ...headers };
    if (injectFault) {
      requestHeaders['X-Fault-Inject'] = '503';
    }

    const payload = JSON.stringify({
      channel: `load-test-${Math.floor(Math.random() * 5)}`,
      message: `Connector resilience test message ${uniqueId('msg')}`,
    });

    const start = Date.now();
    const res = http.post(`${baseApi}/connectors/slack/messages`, payload, {
      headers: requestHeaders,
      tags: { name: 'slack_message', provider: 'slack' },
      timeout: '10s',
    });
    const elapsed = Date.now() - start;

    recordMetrics(res, elapsed, injectFault);
  });
}

// --- HubSpot reads (20%) ---
function hubspotRead(baseApi, headers, injectFault) {
  group('connector: hubspot read', () => {
    const requestHeaders = { ...headers };
    if (injectFault) {
      requestHeaders['X-Fault-Inject'] = '503';
    }

    const resources = ['contacts', 'companies', 'deals'];
    const resource = resources[Math.floor(Math.random() * resources.length)];

    const start = Date.now();
    const res = http.get(`${baseApi}/connectors/hubspot/${resource}`, {
      headers: requestHeaders,
      tags: { name: 'hubspot_read', provider: 'hubspot' },
      timeout: '10s',
    });
    const elapsed = Date.now() - start;

    recordMetrics(res, elapsed, injectFault);
  });
}

// --- Shared metric recording ---
function recordMetrics(res, elapsed, faultInjected) {
  connectorLatency.add(elapsed);

  // Timeout enforcement
  if (elapsed >= 10000) {
    timeoutViolations.add(1);
  }

  check(res, {
    'connector: no timeout violation': () => elapsed < 10000,
  });

  const isError = res.status >= 400;
  connectorErrors.add(isError);

  // If fault was injected and we got a 503, circuit breaker should eventually absorb
  if (faultInjected && res.status === 503) {
    circuitBreakerAbsorbed.add(1);
  }

  // For non-fault-injected requests, expect success
  if (!faultInjected) {
    check(res, {
      'connector: success (no fault)': (r) => r.status < 500,
    });
    errorRate.add(res.status >= 400);
  }
}

export function teardown(data) {
  console.log('Connector resilience test complete.');
  console.log(`Circuit breaker absorbed faults tracked via connector_circuit_breaker_absorbed metric.`);
}

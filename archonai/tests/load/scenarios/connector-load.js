// ArchonAI Load Test: Connector Load & Retry Behavior
// Validates concurrent connector operations and retry resilience.
// Target: p95 < 800ms, error < 3%.

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';
import { BASE_URL, API_VERSION, TEST_USERS } from '../lib/config.js';
import { registerUser, loginUser, authHeaders } from '../lib/auth.js';
import { errorRate, uniqueId, thinkTime } from '../lib/helpers.js';
import { CONNECTOR_THRESHOLDS } from '../lib/thresholds.js';

const retryAttempts = new Counter('archon_connector_retries');
const connectorLatency = new Trend('archon_connector_latency', true);

export const options = {
  scenarios: {
    connector_concurrent: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: 25 },
        { duration: '2m', target: 50 },
        { duration: '1m', target: 75 },
        { duration: '2m', target: 75 },
        { duration: '30s', target: 0 },
      ],
      gracefulRampDown: '10s',
    },
  },
  thresholds: CONNECTOR_THRESHOLDS,
};

export function setup() {
  registerUser(TEST_USERS.operator);
  const session = loginUser(TEST_USERS.operator);
  return { token: session.token };
}

export default function (data) {
  const headers = authHeaders(data.token);
  const baseApi = `${BASE_URL}/api/${API_VERSION}/connectors`;

  // Randomly select a connector provider and operation
  const providers = ['salesforce', 'hubspot', 'quickbooks', 'slack'];
  const provider = providers[Math.floor(Math.random() * providers.length)];

  const operations = [
    checkConnectorStatus,
    queryConnectorData,
    fetchConnectorRecords,
  ];
  const operation = operations[Math.floor(Math.random() * operations.length)];

  operation(baseApi, provider, headers);
  thinkTime(0.5, 0.3);
}

function checkConnectorStatus(baseApi, provider, headers) {
  const res = http.get(`${baseApi}/${provider}/status`, {
    headers,
    tags: { name: `connector_status_${provider}` },
  });

  connectorLatency.add(res.timings.duration);
  check(res, {
    [`${provider} status: valid response`]: (r) => r.status < 500,
  });
  errorRate.add(res.status >= 500);
}

function queryConnectorData(baseApi, provider, headers) {
  // Provider-specific data queries
  const endpoints = {
    salesforce: ['accounts', 'contacts', 'opportunities'],
    hubspot: ['contacts', 'deals'],
    quickbooks: ['invoices', 'transactions'],
    slack: ['channels'],
  };

  const providerEndpoints = endpoints[provider] || ['status'];
  const endpoint = providerEndpoints[Math.floor(Math.random() * providerEndpoints.length)];

  const res = http.get(`${baseApi}/${provider}/${endpoint}`, {
    headers,
    tags: { name: `connector_query_${provider}` },
  });

  connectorLatency.add(res.timings.duration);

  // Simulate retry behavior on 5xx or timeout
  if (res.status >= 500 || res.timings.duration > 3000) {
    retryAttempts.add(1);
    sleep(0.5); // Back off

    const retryRes = http.get(`${baseApi}/${provider}/${endpoint}`, {
      headers,
      tags: { name: `connector_retry_${provider}` },
    });
    connectorLatency.add(retryRes.timings.duration);
    errorRate.add(retryRes.status >= 500);
  } else {
    errorRate.add(res.status >= 500);
  }
}

function fetchConnectorRecords(baseApi, provider, headers) {
  const recordTypes = {
    salesforce: 'Account',
    hubspot: 'contact',
    quickbooks: 'Invoice',
    slack: 'message',
  };

  const recordType = recordTypes[provider] || 'record';

  const res = http.get(`${baseApi}/${provider}/records?type=${recordType}&limit=50`, {
    headers,
    tags: { name: `connector_records_${provider}` },
  });

  connectorLatency.add(res.timings.duration);
  check(res, {
    [`${provider} records: valid response`]: (r) => r.status < 500,
  });
  errorRate.add(res.status >= 500);
}

export function teardown(data) {
  console.log('Connector load test complete.');
}

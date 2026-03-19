// ArchonAI k6 Shared Helpers
// Utility functions used across all load test scenarios.

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

// Custom metrics
export const errorRate = new Rate('archon_error_rate');
export const crossTenantViolations = new Counter('archon_cross_tenant_violations');
export const rateLimitHits = new Counter('archon_rate_limit_429s');
export const compositionLatency = new Trend('archon_composition_latency', true);

// Make an API request with standard error tracking.
export function apiRequest(method, url, body, params = {}) {
  let res;
  const opts = { ...params };

  switch (method.toUpperCase()) {
    case 'GET':
      res = http.get(url, opts);
      break;
    case 'POST':
      res = http.post(url, body ? JSON.stringify(body) : null, opts);
      break;
    case 'PUT':
      res = http.put(url, body ? JSON.stringify(body) : null, opts);
      break;
    case 'PATCH':
      res = http.patch(url, body ? JSON.stringify(body) : null, opts);
      break;
    case 'DELETE':
      res = http.del(url, body ? JSON.stringify(body) : null, opts);
      break;
    default:
      throw new Error(`Unsupported method: ${method}`);
  }

  const isError = res.status >= 400 && res.status !== 429;
  errorRate.add(isError);

  if (res.status === 429) {
    rateLimitHits.add(1);
  }

  return res;
}

// Verify response contains only data for the expected tenant.
export function assertTenantIsolation(res, expectedTenantId) {
  if (res.status !== 200) return true; // Skip non-success responses

  try {
    const body = JSON.parse(res.body);
    const items = Array.isArray(body) ? body : (body.items || body.data || [body]);

    for (const item of items) {
      if (item.tenantId && item.tenantId !== expectedTenantId) {
        crossTenantViolations.add(1);
        console.error(
          `TENANT ISOLATION VIOLATION: Expected ${expectedTenantId}, got ${item.tenantId}`
        );
        return false;
      }
    }
  } catch {
    // Non-JSON or unparseable — not a violation
  }

  return true;
}

// Generate a unique ID for test resources.
export function uniqueId(prefix = 'lt') {
  const ts = Date.now().toString(36);
  const rand = Math.random().toString(36).substring(2, 8);
  return `${prefix}-${ts}-${rand}`;
}

// Controlled think time between requests (jittered).
export function thinkTime(baseSec = 1, jitterSec = 0.5) {
  sleep(baseSec + Math.random() * jitterSec);
}

// Parse JSON response safely.
export function parseBody(res) {
  try {
    return JSON.parse(res.body);
  } catch {
    return null;
  }
}

// Standard check for successful API response.
export function checkSuccess(res, name, expectedStatus = 200) {
  return check(res, {
    [`${name}: status ${expectedStatus}`]: (r) => r.status === expectedStatus,
    [`${name}: response time OK`]: (r) => r.timings.duration < 5000,
  });
}

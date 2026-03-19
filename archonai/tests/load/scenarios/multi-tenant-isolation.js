// ArchonAI Load Test: Multi-Tenant Isolation Under Load
// Verifies zero cross-tenant data leakage with 10 tenants × 10 VUs.
// CRITICAL: archon_cross_tenant_violations must be exactly 0.

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { SharedArray } from 'k6/data';
import { BASE_URL, API_VERSION, TENANT_COUNT, tenantUser } from '../lib/config.js';
import { registerUser, loginUser, authHeaders } from '../lib/auth.js';
import {
  errorRate,
  crossTenantViolations,
  assertTenantIsolation,
  uniqueId,
  thinkTime,
} from '../lib/helpers.js';
import { MULTI_TENANT_THRESHOLDS } from '../lib/thresholds.js';

export const options = {
  scenarios: {
    tenant_isolation: {
      executor: 'per-vu-iterations',
      vus: TENANT_COUNT * 10,  // 10 tenants × 10 VUs = 100 VUs
      iterations: 20,          // Each VU runs 20 iterations
      maxDuration: '10m',
    },
  },
  thresholds: MULTI_TENANT_THRESHOLDS,
};

export function setup() {
  // Register and login all tenant users, store tokens.
  const tenantTokens = {};

  for (let t = 0; t < TENANT_COUNT; t++) {
    const user = tenantUser(t);
    registerUser(user);
    try {
      const session = loginUser(user);
      tenantTokens[t] = {
        token: session.token,
        tenantId: user.tenantId,
        email: user.email,
      };
    } catch (e) {
      console.error(`Failed to setup tenant ${t}: ${e}`);
    }
  }

  return { tenantTokens };
}

export default function (data) {
  // Determine which tenant this VU represents (VU ID → tenant index).
  const tenantIndex = (__VU - 1) % TENANT_COUNT;
  const tenant = data.tenantTokens[tenantIndex];

  if (!tenant) {
    console.error(`No token for tenant index ${tenantIndex}`);
    sleep(1);
    return;
  }

  const headers = authHeaders(tenant.token);
  const baseApi = `${BASE_URL}/api/${API_VERSION}`;

  // Operation 1: List agents — must only see own tenant's agents
  group('tenant_list_agents', () => {
    const res = http.get(`${baseApi}/registry/agents`, {
      headers,
      tags: { name: 'tenant_list_agents' },
    });

    check(res, {
      'list agents: status 200': (r) => r.status === 200,
    });

    assertTenantIsolation(res, tenant.tenantId);
    errorRate.add(res.status >= 400);
  });

  thinkTime(0.3, 0.2);

  // Operation 2: Query traces — must only see own tenant's traces
  group('tenant_list_traces', () => {
    const res = http.get(`${baseApi}/traces`, {
      headers,
      tags: { name: 'tenant_list_traces' },
    });

    check(res, {
      'list traces: status 200 or 404': (r) => [200, 404].includes(r.status),
    });

    assertTenantIsolation(res, tenant.tenantId);
    errorRate.add(res.status >= 500);
  });

  thinkTime(0.3, 0.2);

  // Operation 3: Connector status — must be scoped to tenant
  group('tenant_connector_status', () => {
    const providers = ['salesforce', 'hubspot'];
    const provider = providers[Math.floor(Math.random() * providers.length)];

    const res = http.get(`${baseApi}/connectors/${provider}/status`, {
      headers,
      tags: { name: 'tenant_connector_status' },
    });

    assertTenantIsolation(res, tenant.tenantId);
    errorRate.add(res.status >= 500);
  });

  thinkTime(0.3, 0.2);

  // Operation 4: Get current user — must match tenant
  group('tenant_auth_me', () => {
    const res = http.get(`${BASE_URL}/api/auth/me`, {
      headers,
      tags: { name: 'tenant_auth_me' },
    });

    check(res, {
      'auth me: status 200': (r) => r.status === 200,
    });

    // Verify the returned user belongs to the correct tenant
    if (res.status === 200) {
      try {
        const body = JSON.parse(res.body);
        if (body.tenantId && body.tenantId !== tenant.tenantId) {
          crossTenantViolations.add(1);
          console.error(
            `AUTH ME VIOLATION: VU ${__VU} expected tenant ${tenant.tenantId}, got ${body.tenantId}`
          );
        }
      } catch { /* ignore parse errors */ }
    }

    errorRate.add(res.status >= 400);
  });

  thinkTime(0.5, 0.3);

  // Operation 5: Cross-tenant probe — attempt to access another tenant's data
  // by including a different tenant's ID in query params (should be rejected or filtered).
  group('cross_tenant_probe', () => {
    const otherTenantIndex = (tenantIndex + 1) % TENANT_COUNT;
    const otherTenantId = `tenant-${String(otherTenantIndex).padStart(3, '0')}`;

    const res = http.get(`${baseApi}/registry/agents?tenantId=${otherTenantId}`, {
      headers,
      tags: { name: 'cross_tenant_probe' },
    });

    // Should still only return the caller's own data, not the other tenant's
    assertTenantIsolation(res, tenant.tenantId);
  });

  thinkTime(0.5, 0.3);
}

export function teardown(data) {
  console.log('Multi-tenant isolation test complete.');
  console.log('CRITICAL: Check archon_cross_tenant_violations — must be 0.');
}

// ArchonAI Load Test: Authentication Flow
// Validates auth endpoints under load: register, login, token refresh, /me.
// Target: 50→100 VUs, p95 < 200ms, error rate < 1%.

import { sleep } from 'k6';
import { BASE_URL, TEST_USERS } from '../lib/config.js';
import { registerUser, loginUser, refreshToken, getMe } from '../lib/auth.js';
import { errorRate, thinkTime, uniqueId } from '../lib/helpers.js';
import { AUTH_THRESHOLDS } from '../lib/thresholds.js';

export const options = {
  scenarios: {
    auth_ramp: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: 50 },   // Ramp to 50
        { duration: '1m', target: 50 },     // Sustain 50
        { duration: '30s', target: 100 },   // Ramp to 100
        { duration: '2m', target: 100 },    // Sustain 100
        { duration: '30s', target: 0 },     // Ramp down
      ],
      gracefulRampDown: '10s',
    },
  },
  thresholds: AUTH_THRESHOLDS,
};

export function setup() {
  // Register the standard test users before the test run.
  for (const key of Object.keys(TEST_USERS)) {
    registerUser(TEST_USERS[key]);
  }
  return { ready: true };
}

export default function () {
  // Each VU runs a full auth lifecycle:
  // 1. Login → 2. Get profile → 3. Refresh token → 4. Get profile again → 5. Logout-like pause

  const user = TEST_USERS.operator;

  // Step 1: Login
  const session = loginUser(user);
  errorRate.add(!session.token);
  thinkTime(0.5, 0.3);

  // Step 2: Get current user profile
  const meRes = getMe(session.token);
  errorRate.add(meRes.status !== 200);
  thinkTime(0.3, 0.2);

  // Step 3: Refresh the token
  const refreshed = refreshToken(session.refreshToken);
  errorRate.add(!refreshed);
  thinkTime(0.3, 0.2);

  // Step 4: Use refreshed token to get profile
  if (refreshed && refreshed.token) {
    const meRes2 = getMe(refreshed.token);
    errorRate.add(meRes2.status !== 200);
  }

  thinkTime(1, 0.5);
}

export function teardown(data) {
  console.log('Auth flow test complete.');
}

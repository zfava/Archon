// ArchonAI k6 Auth Helpers
// Handles registration, login, token management for load tests.

import http from 'k6/http';
import { check, fail } from 'k6';
import { BASE_URL } from './config.js';

const JSON_HEADERS = { 'Content-Type': 'application/json' };

// Register a test user. Idempotent — ignores 409 (already exists).
export function registerUser(user) {
  const payload = JSON.stringify({
    email: user.email,
    password: user.password,
    displayName: `Load Test ${user.role}`,
    role: user.role,
    tenantId: user.tenantId || undefined,
  });

  const res = http.post(`${BASE_URL}/api/auth/register`, payload, {
    headers: JSON_HEADERS,
    tags: { name: 'auth_register' },
  });

  const ok = check(res, {
    'register: 200 or 201 or 409': (r) => [200, 201, 409].includes(r.status),
  });

  if (!ok) {
    console.error(`Registration failed for ${user.email}: ${res.status} ${res.body}`);
  }

  return res;
}

// Login and return { token, refreshToken, userId }.
export function loginUser(user) {
  const payload = JSON.stringify({
    email: user.email,
    password: user.password,
  });

  const res = http.post(`${BASE_URL}/api/auth/login`, payload, {
    headers: JSON_HEADERS,
    tags: { name: 'auth_login' },
  });

  const ok = check(res, {
    'login: status 200': (r) => r.status === 200,
    'login: has token': (r) => {
      try { return !!JSON.parse(r.body).token; } catch { return false; }
    },
  });

  if (!ok) {
    fail(`Login failed for ${user.email}: ${res.status} ${res.body}`);
  }

  const body = JSON.parse(res.body);
  return {
    token: body.token,
    refreshToken: body.refreshToken,
    userId: body.userId,
  };
}

// Refresh an access token.
export function refreshToken(token) {
  const payload = JSON.stringify({ refreshToken: token });

  const res = http.post(`${BASE_URL}/api/auth/refresh`, payload, {
    headers: JSON_HEADERS,
    tags: { name: 'auth_refresh' },
  });

  check(res, {
    'refresh: status 200': (r) => r.status === 200,
  });

  if (res.status !== 200) return null;

  const body = JSON.parse(res.body);
  return {
    token: body.token,
    refreshToken: body.refreshToken,
  };
}

// Build authorized headers for API calls.
export function authHeaders(token) {
  return {
    'Content-Type': 'application/json',
    Authorization: `Bearer ${token}`,
  };
}

// Get current user info.
export function getMe(token) {
  const res = http.get(`${BASE_URL}/api/auth/me`, {
    headers: authHeaders(token),
    tags: { name: 'auth_me' },
  });

  check(res, {
    'me: status 200': (r) => r.status === 200,
  });

  return res;
}

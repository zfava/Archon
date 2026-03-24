/* eslint-disable react-refresh/only-export-components */
import { createContext, useContext, useState, useCallback, useEffect, type ReactNode } from 'react';

export interface UserInfo {
  id: string;
  email: string;
  displayName: string;
  role: string;
}

export interface OrgInfo {
  id: string;
  name: string;
  slug: string;
}

export interface AuthState {
  user: UserInfo | null;
  org: OrgInfo | null;
  accessToken: string | null;
  isAuthenticated: boolean;
  isLoading: boolean;
}

interface AuthContextValue extends AuthState {
  login: (email: string, password: string) => Promise<boolean>;
  register: (orgName: string, email: string, password: string, displayName: string) => Promise<boolean>;
  logout: () => Promise<void>;
  getAccessToken: () => Promise<string | null>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

const TOKEN_KEY = 'archonai_access_token';
const REFRESH_KEY = 'archonai_refresh_token';
const EXPIRY_KEY = 'archonai_token_expiry';
const AUTH_API = import.meta.env.VITE_API_BASE_URL
  ? `${import.meta.env.VITE_API_BASE_URL.replace(/\/v1$/, '')}/auth`
  : '/api/auth';

async function authRequest<T>(path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' };
  const token = localStorage.getItem(TOKEN_KEY);
  if (token) headers['Authorization'] = `Bearer ${token}`;

  const res = await fetch(`${AUTH_API}${path}`, {
    method: 'POST',
    headers,
    body: body ? JSON.stringify(body) : undefined,
  });

  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(text || `${res.status} ${res.statusText}`);
  }
  return res.json();
}

interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAtUtc: string;
  user: UserInfo;
  organization: OrgInfo;
}

function storeTokens(accessToken: string, refreshToken: string, expiresAtUtc: string) {
  localStorage.setItem(TOKEN_KEY, accessToken);
  localStorage.setItem(REFRESH_KEY, refreshToken);
  localStorage.setItem(EXPIRY_KEY, expiresAtUtc);
}

function clearTokens() {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(REFRESH_KEY);
  localStorage.removeItem(EXPIRY_KEY);
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({
    user: null,
    org: null,
    accessToken: null,
    isAuthenticated: false,
    isLoading: true,
  });

  const setAuth = useCallback((user: UserInfo, org: OrgInfo, accessToken: string) => {
    setState({ user, org, accessToken, isAuthenticated: true, isLoading: false });
  }, []);

  const clearAuth = useCallback(() => {
    clearTokens();
    setState({ user: null, org: null, accessToken: null, isAuthenticated: false, isLoading: false });
  }, []);

  // Restore session on mount
  useEffect(() => {
    const token = localStorage.getItem(TOKEN_KEY);
    if (!token) {
      queueMicrotask(() => setState(s => ({ ...s, isLoading: false })));
      return;
    }

    const meUrl = `${AUTH_API}/me`;
    fetch(meUrl, {
      method: 'GET',
      headers: { 'Authorization': `Bearer ${token}`, 'Content-Type': 'application/json' },
    })
      .then(async res => {
        if (!res.ok) throw new Error('Session expired');
        const data: AuthResponse = await res.json();
        setState({
          user: data.user,
          org: data.organization,
          accessToken: token,
          isAuthenticated: true,
          isLoading: false,
        });
      })
      .catch(() => {
        clearTokens();
        setState({ user: null, org: null, accessToken: null, isAuthenticated: false, isLoading: false });
      });
  }, []);

  // Listen for auth:expired events dispatched by the API client on 401 responses
  useEffect(() => {
    const handleAuthExpired = () => {
      clearAuth();
    };
    window.addEventListener('auth:expired', handleAuthExpired);
    return () => window.removeEventListener('auth:expired', handleAuthExpired);
  }, [clearAuth]);

  const login = useCallback(async (email: string, password: string): Promise<boolean> => {
    try {
      const data = await authRequest<AuthResponse>('/login', { email, password });
      storeTokens(data.accessToken, data.refreshToken, data.expiresAtUtc);
      setAuth(data.user, data.organization, data.accessToken);
      return true;
    } catch {
      return false;
    }
  }, [setAuth]);

  const register = useCallback(async (
    orgName: string, email: string, password: string, displayName: string
  ): Promise<boolean> => {
    try {
      const data = await authRequest<AuthResponse>('/register', {
        organizationName: orgName, email, password, displayName,
      });
      storeTokens(data.accessToken, data.refreshToken, data.expiresAtUtc);
      setAuth(data.user, data.organization, data.accessToken);
      return true;
    } catch {
      return false;
    }
  }, [setAuth]);

  const logout = useCallback(async () => {
    try {
      await authRequest('/logout');
    } catch { /* best effort */ }
    clearAuth();
  }, [clearAuth]);

  const getAccessToken = useCallback(async (): Promise<string | null> => {
    const token = localStorage.getItem(TOKEN_KEY);
    const expiry = localStorage.getItem(EXPIRY_KEY);

    if (token && expiry) {
      const expiresAt = new Date(expiry).getTime();
      const bufferMs = 60_000; // refresh 1 minute before expiry
      if (Date.now() < expiresAt - bufferMs) return token;
    }

    // Try refresh
    const refreshToken = localStorage.getItem(REFRESH_KEY);
    if (!refreshToken) {
      clearAuth();
      return null;
    }

    try {
      const data = await authRequest<{ accessToken: string; refreshToken: string; expiresAtUtc: string }>(
        '/refresh', { refreshToken }
      );
      storeTokens(data.accessToken, data.refreshToken, data.expiresAtUtc);
      setState(s => ({ ...s, accessToken: data.accessToken }));
      return data.accessToken;
    } catch {
      clearAuth();
      return null;
    }
  }, [clearAuth]);

  return (
    <AuthContext.Provider value={{ ...state, login, register, logout, getAccessToken }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}

# Session Model

## Overview

ArchonAI sessions are stateless on the backend (JWT-based) with client-side token management. Session lifecycle is governed by access token expiry and refresh token rotation.

## Session Lifecycle

```
Registration/Login
    │
    ▼
┌──────────────────┐
│ Active Session    │ ← Access token valid
│ (30 min window)   │
└────────┬─────────┘
         │ Access token expires
         ▼
┌──────────────────┐
│ Refresh Window    │ ← Refresh token still valid (7 days)
│                    │   Client auto-refreshes before expiry
└────────┬─────────┘
         │ Refresh token expires or revoked
         ▼
┌──────────────────┐
│ Session Ended     │ → Redirect to /login
└──────────────────┘
```

## Client-Side Session Management

### Token Storage

Tokens are stored in `localStorage`:
- `archonai_access_token` — Current JWT access token
- `archonai_refresh_token` — Current refresh token
- `archonai_token_expiry` — ISO 8601 expiry timestamp

### Automatic Refresh

The `getAccessToken()` function in `AuthContext`:
1. Checks if the current access token expires within 60 seconds
2. If still valid, returns it immediately
3. If expiring soon, uses the refresh token to obtain new tokens
4. If refresh fails, clears session and redirects to login

### 401 Handling

The API client (`configureApiAuth`) registers an `onUnauthorized` callback that:
1. Calls `logout()` to clear local state
2. Navigates to `/login`

This handles edge cases where a token is revoked server-side.

## Session Restoration

On page load, `AuthProvider` attempts to restore the session:
1. Checks for an existing access token in `localStorage`
2. Calls `GET /api/auth/me` with the token
3. If successful, restores user/org state without requiring re-login
4. If failed, clears tokens and shows login

## Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| `Authentication:AccessTokenLifetimeMinutes` | 30 | JWT access token lifetime |
| `Authentication:RefreshTokenLifetimeDays` | 7 | Refresh token lifetime |
| `Security:Jwt:ClockSkewSeconds` | 60 | Allowed clock drift |

## Security Considerations

- **No httpOnly cookies**: Tokens are in `localStorage` for SPA compatibility. Consider moving to `httpOnly` cookies for XSS mitigation in production.
- **Refresh rotation**: Each refresh token is single-use. If a stolen refresh token is used, the legitimate user's next refresh will fail, signaling compromise.
- **Concurrent sessions**: Multiple browser tabs share the same `localStorage` tokens. A logout in one tab affects all tabs.

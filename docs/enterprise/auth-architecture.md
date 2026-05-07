# Authentication Architecture

## Overview

ArchonAI uses a JWT-based authentication system with PBKDF2 password hashing, refresh token rotation, and tenant-scoped claims. The architecture is designed to be extensible to OIDC/SAML/SSO federation without structural changes.

## Components

### Backend

| Component | Location | Responsibility |
|-----------|----------|----------------|
| `AuthenticationService` | `ArchonAI.Identity` | Login, register, refresh, logout, invite/accept |
| `TokenService` | `ArchonAI.Identity` | JWT access token generation with tenant claims |
| `TenantResolutionMiddleware` | `ArchonAI.Api.Security` | Extracts `tenant_id` from JWT and sets `IMultiTenantContext` |
| In-memory stores | `ArchonAI.Identity.Stores` | User, Org, Membership, RefreshToken, InviteToken storage |

### Frontend

| Component | Location | Responsibility |
|-----------|----------|----------------|
| `AuthProvider` | `src/auth/AuthContext.tsx` | React context for auth state, login/register/logout |
| `ProtectedRoute` | `src/auth/ProtectedRoute.tsx` | Redirects unauthenticated users to `/login` |
| `AuthApiWiring` | `src/auth/AuthApiWiring.tsx` | Connects auth context to API client |
| `configureApiAuth` | `src/api/client.ts` | Attaches Bearer tokens and handles 401 |

## Auth Flow

```
[User] → POST /api/auth/login { email, password }
       ← { accessToken, refreshToken, expiresAtUtc, user, organization }

[User] → GET /api/v1/* (Authorization: Bearer <accessToken>)
       ← 200 (or 401 if expired/invalid)

[User] → POST /api/auth/refresh { refreshToken }
       ← { accessToken, refreshToken, expiresAtUtc }
         (old refresh token is revoked — rotation)

[User] → POST /api/auth/logout (Authorization: Bearer <accessToken>)
       ← 200 (all refresh tokens for user revoked)
```

## Token Structure

### Access Token (JWT)

Claims:
- `sub` — User ID (GUID)
- `email` — User email
- `name` — Display name
- `org_id` — Organization ID (GUID)
- `org_slug` — Organization slug
- `tenant_id` — Tenant ID (same as org_id)
- `role` — User role (Admin, Operator, Viewer)

Signed with HMAC-SHA256. Lifetime: 30 minutes (configurable).

### Refresh Token

- 256-bit cryptographically random value
- Stored as SHA-256 hash in the token store
- Lifetime: 7 days (configurable)
- Single-use with rotation (revoked on use, new token issued)

## Password Storage

- PBKDF2 with SHA-256, 100,000 iterations
- 128-bit random salt per password
- Constant-time comparison via `CryptographicOperations.FixedTimeEquals`

## API Endpoints

| Method | Path | Auth Required | Description |
|--------|------|---------------|-------------|
| POST | `/api/auth/register` | No | Create org + admin user |
| POST | `/api/auth/login` | No | Authenticate and receive tokens |
| POST | `/api/auth/refresh` | No | Exchange refresh token for new tokens |
| POST | `/api/auth/logout` | Yes | Revoke all refresh tokens |
| GET | `/api/auth/me` | Yes | Get current user + org info |
| POST | `/api/auth/invite` | Yes (Admin) | Generate invite token for email |
| POST | `/api/auth/accept-invite` | No | Accept invite and create account |

## Security Properties

- No plaintext passwords stored
- Refresh tokens are one-time-use (rotation prevents replay)
- Logout invalidates all sessions
- JWT clock skew tolerance: 60 seconds
- Signing key loaded from configuration or environment variable

## Future Extension Points

- **OIDC/SAML**: Add external identity provider support by issuing the same JWT format after federation callback
- **MFA**: Add step between credential verification and token issuance
- **API Keys**: Alternative token type for service-to-service auth
- **Session binding**: Tie refresh tokens to device/IP fingerprint

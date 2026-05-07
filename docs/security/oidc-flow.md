# OIDC Authentication Flow

## Overview

ArchonAI implements an authorization code flow with PKCE (RFC 7636) and server-side session binding for enterprise OIDC federation. All security-sensitive parameters (nonce, code_verifier) are held server-side and never exposed to the client.

## Flow Sequence

```
Client              ArchonAI Server              Identity Provider (IdP)
  │                       │                              │
  │  GET /login?tenant=x  │                              │
  │──────────────────────>│                              │
  │                       │  Generate: state, nonce,     │
  │                       │  code_verifier, code_challenge│
  │                       │  Store in OidcLoginSession   │
  │  {authorizeUrl, state}│                              │
  │<──────────────────────│                              │
  │                       │                              │
  │  Redirect to IdP authorizeUrl (with state, nonce,    │
  │  code_challenge in URL)                              │
  │─────────────────────────────────────────────────────>│
  │                       │                              │
  │  Redirect back with code + state                     │
  │<─────────────────────────────────────────────────────│
  │                       │                              │
  │  POST /callback       │                              │
  │  {code, state, orgId} │                              │
  │──────────────────────>│                              │
  │                       │  Consume session (by state)  │
  │                       │  Validate: expiry, org match │
  │                       │                              │
  │                       │  Token exchange (back-channel)│
  │                       │  code + code_verifier (server)│
  │                       │─────────────────────────────>│
  │                       │  {id_token, access_token}    │
  │                       │<─────────────────────────────│
  │                       │                              │
  │                       │  Validate id_token:          │
  │                       │  - JWKS signature            │
  │                       │  - issuer, audience, lifetime│
  │                       │  - nonce == server nonce     │
  │                       │                              │
  │                       │  JIT provision if needed     │
  │                       │  Issue internal JWT          │
  │  {accessToken, user}  │                              │
  │<──────────────────────│                              │
```

## Server-Side Session Binding

Each `/login` request creates an `OidcLoginSession` record stored server-side:

| Field           | Purpose                                              |
|-----------------|------------------------------------------------------|
| `State`         | Lookup key; returned to client for redirect correlation |
| `Nonce`         | Validated against id_token nonce claim (anti-replay)  |
| `CodeVerifier`  | Used in back-channel token exchange (PKCE)            |
| `OrganizationId`| Binds the callback to the originating tenant          |
| `CreatedAtUtc`  | Audit trail                                          |
| `ExpiresAtUtc`  | Hard expiration (default 300s from `OidcOptions`)     |

## Security Properties

### Anti-CSRF (State Validation)
- State is generated server-side with 256-bit cryptographic randomness
- Callback looks up state in the server store — attacker-crafted states are rejected
- State is consumed (deleted) on use — prevents replay

### Anti-Replay (Nonce Validation)
- Nonce is generated server-side and included in the authorize URL
- The IdP embeds this nonce in the id_token
- On callback, the id_token nonce is validated against the server-stored nonce
- The nonce is never sent to or accepted from the client

### PKCE (Code Verifier Binding)
- Code verifier is generated server-side and stored in the login session
- Code challenge (SHA-256 hash of verifier) is sent to the IdP in the authorize URL
- On callback, the server retrieves the verifier from the session and sends it in the back-channel token exchange
- The IdP validates the verifier against the original challenge

### Expiration
- Login sessions expire after `OidcOptions.StateExpirationSeconds` (default: 300s)
- Expired sessions are rejected at callback time even if the state matches

### Organization Binding
- The callback validates that the organization ID matches the one from the login session
- Prevents cross-tenant callback injection

## What the Client Receives

The client only receives:
- `AuthorizeUrl` — the full IdP authorize URL to redirect to
- `State` — for correlating the redirect callback
- `OrganizationId` — for the callback request

The client **never** receives: nonce, code_verifier, code_challenge.

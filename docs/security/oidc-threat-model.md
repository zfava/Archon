# OIDC Threat Model

## Scope

This document covers threats to the OIDC authorization code + PKCE flow in ArchonAI, focusing on the login initiation → callback → token exchange path.

## Threat Matrix

### T1: CSRF / Cross-Site Request Forgery on Callback

**Attack**: Attacker crafts a callback URL with their own authorization code, tricking a victim's browser into completing the OIDC flow with the attacker's identity.

**Mitigation**: Server-side state parameter. The state is generated at `/login`, stored in `OidcLoginSession`, and validated at `/callback` by atomic consumption (lookup + delete). An attacker cannot forge a valid state because:
- State is 256-bit cryptographically random
- State must exist in the server store (not client-asserted)
- State is single-use (consumed on first callback)

**Status**: Mitigated.

### T2: Authorization Code Replay

**Attack**: Attacker captures a valid authorization code and replays it against the callback endpoint.

**Mitigation**:
1. The OIDC login session is consumed (deleted) on first use — a second callback with the same state is rejected
2. Authorization codes are single-use at the IdP level (per OAuth 2.0 spec)
3. PKCE code_verifier is required for token exchange — the attacker would need the server-held verifier

**Status**: Mitigated.

### T3: Nonce Replay / Token Injection

**Attack**: Attacker obtains a valid id_token from a previous session and submits it in a new callback flow.

**Mitigation**: The nonce in the id_token is validated against the server-stored nonce from the original login session. Each login generates a unique nonce, so a replayed id_token's nonce will not match the current session's nonce.

**Previous vulnerability**: The callback previously extracted the nonce FROM the id_token and used it as the expected value — making the validation circular (always passing). This has been fixed.

**Status**: Mitigated.

### T4: PKCE Downgrade / Code Interception

**Attack**: Attacker intercepts the authorization code and attempts token exchange without the PKCE code_verifier.

**Mitigation**: The code_verifier is stored server-side in the login session and injected into the back-channel token exchange. It is never exposed to the client or transmitted over the front channel. The IdP rejects token exchange requests without a valid code_verifier matching the original code_challenge.

**Previous vulnerability**: The code_verifier was returned to the client in the login response, creating a window where it could be intercepted. It is now server-side only.

**Status**: Mitigated.

### T5: Cross-Tenant Callback Injection

**Attack**: Attacker initiates OIDC login for tenant A, then submits the callback with tenant B's organization ID to gain access to a different tenant.

**Mitigation**: The organization ID is stored in the login session at creation time. The callback validates that the submitted organization ID matches the session's organization ID.

**Status**: Mitigated.

### T6: Session Expiration Bypass

**Attack**: Attacker uses a stale authorization code/state long after the login was initiated.

**Mitigation**: Login sessions have a hard expiration (`StateExpirationSeconds`, default 300s). The callback checks `ExpiresAtUtc` and rejects expired sessions.

**Status**: Mitigated.

### T7: Client Secret Exposure

**Attack**: Client secret leaked through front-channel responses.

**Mitigation**: Client secrets are only used in the back-channel token exchange (server-to-IdP). They are never included in authorize URLs or client responses.

**Status**: Mitigated.

## Residual Risks

### R1: In-Memory Session Store Limitations
The current `InMemoryOidcLoginSessionStore` does not survive server restarts and does not work across multiple server instances without sticky sessions. For production horizontal scaling, a distributed store (Redis, database) should replace the in-memory implementation.

### R2: Session Store Growth
Abandoned login sessions (user starts login but never completes callback) accumulate in memory. A background cleanup task should periodically purge expired sessions. This is acceptable for current scale but should be addressed before high-volume deployment.

### R3: IdP Token Endpoint Trust
The back-channel token exchange trusts the IdP's token endpoint response. A compromised IdP could issue malicious id_tokens. This is inherent to OIDC federation and mitigated by JWKS signature verification and issuer validation.

### R4: id_token Algorithm Restriction
The system rejects `alg: none` but does not maintain an explicit allowlist of acceptable algorithms. Consider restricting to RS256/RS384/RS512/ES256/ES384/ES512 only.

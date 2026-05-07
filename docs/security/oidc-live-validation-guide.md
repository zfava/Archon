# OIDC Live IdP Validation Guide

This guide describes how to set up, run, and interpret the live integration tests that validate ArchonAI's OIDC federation against real identity providers (Okta, Microsoft Entra ID, Auth0).

## Overview

The live IdP validation tests exercise the full OIDC flow against real identity provider tenants. Unlike unit tests that mock IdP responses, these tests verify that:

- Authorize URLs are well-formed and point to reachable endpoints
- Token exchange via Resource Owner Password Credentials (ROPC) succeeds
- id_tokens contain the claims required for JIT user provisioning
- State replay prevention works correctly
- Expired state rejection works correctly

All tests are gated behind environment variables and silently skip when credentials are absent, so they never break the normal CI pipeline.

## IdP Setup

### Okta

1. **Create an Okta Developer account** at https://developer.okta.com/signup/.
2. In the Admin Console, go to **Applications > Create App Integration**.
3. Select **OIDC - OpenID Connect** and **Web Application**.
4. Configure:
   - **Sign-in redirect URIs**: `http://localhost/callback`
   - **Grant types**: Authorization Code, Resource Owner Password (enable under Advanced settings)
   - **Scopes**: `openid`, `profile`, `email`
5. Note the **Client ID** and **Client secret**.
6. Note your Okta domain (e.g., `dev-12345678.okta.com`).
7. Go to **Directory > People** and create a test user with a known email and password.
8. Assign the test user to the application.

**Required environment variables:**

| Variable | Example |
|---|---|
| `ARCHONAI_TEST_OKTA_DOMAIN` | `dev-12345678.okta.com` |
| `ARCHONAI_TEST_OKTA_CLIENT_ID` | `0oa1bcdef2ghijk3l4m5` |
| `ARCHONAI_TEST_OKTA_CLIENT_SECRET` | `AbCdEf...` |
| `ARCHONAI_TEST_OKTA_TEST_USER_EMAIL` | `testuser@example.com` |
| `ARCHONAI_TEST_OKTA_TEST_USER_PASSWORD` | `SecureP@ssw0rd` |

### Microsoft Entra ID (Azure AD)

1. Sign in to the **Azure Portal** at https://portal.azure.com.
2. Go to **Microsoft Entra ID > App registrations > New registration**.
3. Configure:
   - **Redirect URI (Web)**: `http://localhost/callback`
   - **Supported account types**: Accounts in this organizational directory only
4. Under **Certificates & secrets**, create a new client secret. Note the value.
5. Under **Authentication**, enable **Allow public client flows** (required for ROPC).
6. Under **API permissions**, ensure `openid`, `profile`, and `email` are granted.
7. Note the **Application (client) ID** and **Directory (tenant) ID** from the Overview page.
8. Create a test user in **Users > New user** with a known password. The user must not require MFA for these tests.

**Required environment variables:**

| Variable | Example |
|---|---|
| `ARCHONAI_TEST_ENTRA_TENANT_ID` | `a1b2c3d4-e5f6-7890-abcd-ef1234567890` |
| `ARCHONAI_TEST_ENTRA_CLIENT_ID` | `12345678-abcd-efgh-ijkl-mnopqrstuvwx` |
| `ARCHONAI_TEST_ENTRA_CLIENT_SECRET` | `~AbCdEf...` |
| `ARCHONAI_TEST_ENTRA_TEST_USER_EMAIL` | `testuser@contoso.onmicrosoft.com` |
| `ARCHONAI_TEST_ENTRA_TEST_USER_PASSWORD` | `SecureP@ssw0rd` |

### Auth0

1. Sign up at https://auth0.com and create a tenant.
2. Go to **Applications > Create Application**, select **Regular Web Application**.
3. In **Settings**, configure:
   - **Allowed Callback URLs**: `http://localhost/callback`
   - Note the **Domain**, **Client ID**, and **Client Secret**.
4. Under **Advanced Settings > Grant Types**, enable **Password**.
5. Under **Tenant Settings > API Authorization Settings**, set **Default Directory** to `Username-Password-Authentication`.
6. Go to **User Management > Users** and create a test user with a known email and password.

**Required environment variables:**

| Variable | Example |
|---|---|
| `ARCHONAI_TEST_AUTH0_DOMAIN` | `my-tenant.us.auth0.com` |
| `ARCHONAI_TEST_AUTH0_CLIENT_ID` | `AbCdEfGhIjKlMnOpQrStUvWxYz` |
| `ARCHONAI_TEST_AUTH0_CLIENT_SECRET` | `1234567890abcdef...` |
| `ARCHONAI_TEST_AUTH0_TEST_USER_EMAIL` | `testuser@example.com` |
| `ARCHONAI_TEST_AUTH0_TEST_USER_PASSWORD` | `SecureP@ssw0rd` |

## Running Tests Locally

### Run all live IdP tests

```bash
# Set environment variables for the IdP(s) you want to test, then:
dotnet test archonai/tests/ArchonAI.Enterprise.Tests/ \
  --filter "Category=LiveIntegration" \
  --logger "trx;LogFileName=live-idp-results.trx" \
  --results-directory ./TestResults
```

### Run tests for a specific IdP

```bash
# Okta only
dotnet test archonai/tests/ArchonAI.Enterprise.Tests/ \
  --filter "FullyQualifiedName~OktaLiveIntegrationTests"

# Entra ID only
dotnet test archonai/tests/ArchonAI.Enterprise.Tests/ \
  --filter "FullyQualifiedName~EntraIdLiveIntegrationTests"

# Auth0 only
dotnet test archonai/tests/ArchonAI.Enterprise.Tests/ \
  --filter "FullyQualifiedName~Auth0LiveIntegrationTests"
```

### Run a single test

```bash
dotnet test archonai/tests/ArchonAI.Enterprise.Tests/ \
  --filter "FullyQualifiedName~OktaOidcLogin_GeneratesValidAuthorizeUrl"
```

## Running Tests via GitHub Actions

1. Go to **Actions > Live IdP Validation** in the GitHub repository.
2. Click **Run workflow**.
3. Select which IdP to test: `okta`, `entra`, `auth0`, or `all`.
4. The workflow requires the following secrets to be configured in the repository:

| Secret Name | Used By |
|---|---|
| `ARCHONAI_TEST_OKTA_DOMAIN` | Okta |
| `ARCHONAI_TEST_OKTA_CLIENT_ID` | Okta |
| `ARCHONAI_TEST_OKTA_CLIENT_SECRET` | Okta |
| `ARCHONAI_TEST_OKTA_TEST_USER_EMAIL` | Okta |
| `ARCHONAI_TEST_OKTA_TEST_USER_PASSWORD` | Okta |
| `ARCHONAI_TEST_ENTRA_TENANT_ID` | Entra ID |
| `ARCHONAI_TEST_ENTRA_CLIENT_ID` | Entra ID |
| `ARCHONAI_TEST_ENTRA_CLIENT_SECRET` | Entra ID |
| `ARCHONAI_TEST_ENTRA_TEST_USER_EMAIL` | Entra ID |
| `ARCHONAI_TEST_ENTRA_TEST_USER_PASSWORD` | Entra ID |
| `ARCHONAI_TEST_AUTH0_DOMAIN` | Auth0 |
| `ARCHONAI_TEST_AUTH0_CLIENT_ID` | Auth0 |
| `ARCHONAI_TEST_AUTH0_CLIENT_SECRET` | Auth0 |
| `ARCHONAI_TEST_AUTH0_TEST_USER_EMAIL` | Auth0 |
| `ARCHONAI_TEST_AUTH0_TEST_USER_PASSWORD` | Auth0 |

Test results (TRX format) are uploaded as workflow artifacts and retained for 30 days.

## Test Descriptions

Each IdP test class contains the same 6 test scenarios:

| # | Test | What It Validates |
|---|---|---|
| 1 | `*OidcLogin_GeneratesValidAuthorizeUrl` | Constructs an authorize URL and verifies it has the correct scheme, host, path, query parameters (client_id, response_type, scope, redirect_uri, state, nonce, code_challenge, code_challenge_method), and that the IdP discovery endpoint is reachable. |
| 2 | `*Callback_WithRealCode_ExchangesToken` | Uses the ROPC grant to obtain real tokens from the IdP and verifies the response contains id_token, access_token, and token_type=Bearer. |
| 3 | `*JitProvisioning_NewUser_CreatesAccount` | Obtains an id_token and verifies it contains the `sub` and `email` claims needed for JIT user provisioning, and that the issuer matches the expected IdP authority. |
| 4 | `*JitProvisioning_ExistingUser_Updates` | Obtains two id_tokens for the same user and verifies both have the same `sub` and `email` claims (proving the second login maps to the same identity, not a duplicate). |
| 5 | `*StateReplay_Blocked` | Creates a login session, consumes it once (succeeds), then attempts a second consume with the same state (returns null, blocking replay). |
| 6 | `*ExpiredState_Rejected` | Creates a login session with an expiration in the past and verifies the expiration check rejects it (session age > 300 seconds). |

## Interpreting Results

### All tests pass

The OIDC integration with the tested IdP is working correctly. The authorize URL construction, token exchange, claim extraction, state replay prevention, and expiration logic are all functioning as expected.

### Tests 1-4 fail (IdP connectivity tests)

- **Check credentials**: Verify the environment variables / GitHub secrets are set correctly.
- **Check IdP configuration**: Ensure the ROPC grant type is enabled, the test user exists, and the redirect URI is registered.
- **Check network**: Ensure the test runner can reach the IdP endpoints (no firewall or proxy issues).
- **Check MFA**: The test user must not have MFA enabled, as ROPC does not support interactive authentication.

### Tests 5-6 fail (state management tests)

These tests exercise the `InMemoryOidcLoginSessionStore` directly and do not require IdP credentials. If they fail:
- Verify the `InMemoryOidcLoginSessionStore` implementation has not regressed.
- Check that `ConsumeAsync` atomically removes the session (single-use semantics).
- Check that the callback endpoint correctly compares `DateTimeOffset.UtcNow` against `session.ExpiresAtUtc`.

### Tests silently skip (no output)

This is expected when the required environment variables are not set. The tests use `if (!_canRun) return;` to silently skip. To confirm, check the TRX output or run with verbose logging:

```bash
dotnet test --filter "Category=LiveIntegration" -v detailed
```

## Security Notes

- Test credentials should be for dedicated test users in isolated IdP tenants, never production accounts.
- The ROPC grant is used only for test automation; production flows use the authorization code grant with PKCE.
- All secrets are stored in GitHub encrypted secrets and are never logged in workflow output.
- The workflow has `permissions: contents: read` and does not write to the repository.

# ArchonAI — Quickstart Guide

Get the governance demo running locally in under 15 minutes.

## Prerequisites

- .NET 10 SDK (x64 or ARM64)
- PostgreSQL 16+
- Node.js 20+

## Setup

### 1. Clone and configure

```bash
git clone <repo-url>
cd archonai
cp .env.example .env
```

### 2. Generate security keys

Open `.env` and set:

```bash
# JWT signing key (minimum 32 characters)
ARCHONAI_JWT_SIGNING_KEY=$(openssl rand -base64 48)

# TOTP MFA encryption key (required in production, falls back to JWT key in dev)
ARCHONAI_TOTP_ENCRYPTION_KEY=$(openssl rand -base64 48)

# PostgreSQL password
POSTGRES_PASSWORD=$(openssl rand -base64 24)
```

### 3. Configure AI providers

Set at least one of the following in `.env`:

```bash
# OpenAI (default provider) — https://platform.openai.com/api-keys
OPENAI_API_KEY=sk-...

# Anthropic — https://console.anthropic.com/settings/keys
ANTHROPIC_API_KEY=sk-ant-...
```

At least one cloud provider key is required for AI-powered features to function.

### 4. Set up PostgreSQL

**Option A — Homebrew (macOS):**
```bash
brew install postgresql@16
brew services start postgresql@16
createdb archonai
createuser archonai -P  # enter the password from POSTGRES_PASSWORD
```

**Option B — Docker:**
```bash
docker run -d \
  --name archonai-pg \
  -e POSTGRES_DB=archonai \
  -e POSTGRES_USER=archonai \
  -e POSTGRES_PASSWORD=<your-password> \
  -p 5432:5432 \
  postgres:16
```

### 5. Disable SSL for local development

Uncomment in `.env`:

```bash
ARCHONAI_POSTGRES_SSL_DISABLE=true
```

This disables TLS for the PostgreSQL connection. Never use this in production.

### 6. Build and run the API

```bash
dotnet build
dotnet run --project src/ArchonAI.Api
```

The API starts on port 8080 by default (configured via `ASPNETCORE_URLS`). Database migrations run automatically on startup via DbUp (26 migration scripts).

### 7. Run the governance demo

```bash
curl -X POST http://localhost:8080/api/v1/demo/governance-loop \
  -H "Content-Type: application/json" \
  -d '{}' | jq
```

### 8. Build the frontend

```bash
cd ../archonai-ui
npm install
npm run build
```

For development with hot reload:

```bash
npm run dev
```

## What You Should See

The governance demo endpoint (`POST /api/v1/demo/governance-loop`) executes a full governance cycle:

1. **Policy evaluation** — The PolicyEngine evaluates the requested action against active governance policies
2. **Trust tier assessment** — TrustTierService determines the trust level for the action scope (6-tier model from observe-only through full policy envelope)
3. **Approval gate** — GatedActionExecutor checks whether the action requires human approval based on the trust tier and action safety classification
4. **Proof trail** — A SHA-256 hash-chained audit entry is recorded, providing an immutable proof trail for the governance decision

The response includes the policy evaluation result, trust tier, approval status, and proof hash.

## Verify

```bash
# API liveness
curl http://localhost:8080/healthz/live | jq

# API readiness (includes database migration status)
curl http://localhost:8080/healthz/ready | jq

# AI runtime diagnostics
curl -H "Authorization: Bearer <token>" \
  http://localhost:8080/api/v1/ai-runtime/environment | jq
```

- **Endpoint count:** 452 HTTP endpoints across 17 endpoint files + Gateway
- **SignalR hubs:** `/hubs/control-plane-dashboard`, `/hubs/inspection`
- **Prometheus metrics:** `/metrics` (requires AdminOnly authorization)

## Troubleshooting

| Issue | Solution |
|-------|----------|
| `NETSDK1004: Assets file not found` | Run `dotnet restore` at the solution level before building |
| Database migration fails | Verify PostgreSQL is running and `.env` credentials match |
| AI requests fail | Ensure at least one of `OPENAI_API_KEY` or `ANTHROPIC_API_KEY` is set |
| Port conflict on 8080 | Set `ASPNETCORE_URLS=http://+:5000` in your environment |

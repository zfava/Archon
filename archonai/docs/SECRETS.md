# ArchonAI — Secret Management

## Overview

ArchonAI uses a zero-trust secret management architecture. **No plaintext secrets
exist in source control, container images, or Helm release history.** All secrets
are injected at runtime through one of three supported providers.

## Secret Providers

### 1. Kubernetes Secrets (Inline) — Default

Secrets are provided via `--set` flags at Helm install/upgrade time and stored as
Kubernetes `Secret` resources. Pods consume them via `envFrom: secretRef`.

```bash
helm install archonai ./deploy/helm/archonai \
  -f ./deploy/helm/archonai/values-saas.yaml \
  --set security.jwtSigningKey="$(openssl rand -base64 48)" \
  --set postgres.username="archonai" \
  --set postgres.password="$(openssl rand -base64 24)"
```

**Rotation:** Update the secret and restart pods, or use `--set security.jwtSigningKeyPrevious`
for zero-downtime JWT key rotation (dual-key validation during rollover).

### 2. External Secrets Operator — Cloud Deployments

Uses the [External Secrets Operator](https://external-secrets.io/) to sync secrets
from cloud vaults into Kubernetes Secrets automatically.

**Supported backends:**
- AWS Secrets Manager
- Azure Key Vault
- GCP Secret Manager
- Any ESO-supported backend

```yaml
# values-private.yaml or customer-overrides.yaml
secrets:
  provider: external-secrets
  externalSecrets:
    enabled: true
    secretStoreRef:
      name: aws-secrets-manager    # Your SecretStore CR name
      kind: ClusterSecretStore
    remoteRefs:
      jwtSigningKey: archonai/prod/jwt-signing-key
      postgresUsername: archonai/prod/db-username
      postgresPassword: archonai/prod/db-password
      modelProviderApiKey: archonai/prod/openai-api-key
    refreshInterval: 1h
```

**Rotation:** Update the secret in your cloud vault. ESO refreshes automatically
at the configured `refreshInterval`.

### 3. HashiCorp Vault — Enterprise Deployments

Uses the [Vault Agent Injector](https://developer.hashicorp.com/vault/docs/platform/k8s/injector)
to mount secrets as files via sidecar. The application watches for file changes
and reloads secrets without pod restart.

```yaml
secrets:
  provider: vault-injector
  vault:
    role: archonai-prod
    secretPath: secret/data/archonai/production
    dbSecretPath: secret/data/archonai/production/db
```

**Rotation:** Update the secret in Vault. The Vault Agent sidecar detects changes
and rewrites the file. `FileSecretProvider` watches for changes and triggers
rotation callbacks (e.g., JWT dual-key validation, DB connection pool refresh).

## JWT Key Rotation

ArchonAI supports zero-downtime JWT signing key rotation:

1. **Generate new key:** `openssl rand -base64 48`
2. **Set previous key:** Move the current key to `ARCHONAI_JWT_SIGNING_KEY_PREVIOUS`
3. **Set new key:** Update `ARCHONAI_JWT_SIGNING_KEY` with the new value
4. **Deploy:** Both keys are valid for token validation during rollover
5. **Clean up:** After all tokens signed with the old key expire, remove the previous key

### Inline mode
```bash
helm upgrade archonai ./deploy/helm/archonai \
  --set security.jwtSigningKey="<new-key>" \
  --set security.jwtSigningKeyPrevious="<old-key>" \
  --reuse-values
```

### Vault mode
The `RotatingJwtSecurityKeyProvider` automatically handles dual-key validation
when the Vault Agent sidecar updates the secret file.

## Local Development (Docker Compose)

1. Copy the example environment file:
   ```bash
   cp .env.example .env
   ```

2. Generate required secrets:
   ```bash
   # JWT signing key (minimum 32 characters)
   echo "ARCHONAI_JWT_SIGNING_KEY=$(openssl rand -base64 48)" >> .env

   # Database password
   echo "POSTGRES_PASSWORD=$(openssl rand -base64 24)" >> .env
   ```

3. Add API keys as needed (OpenAI, Anthropic, etc.)

4. Start services:
   ```bash
   docker compose up
   ```

**The `.env` file is in `.gitignore` — it is never committed.**

## Application Architecture

### ISecretProvider Interface
```
ISecretProvider
├── EnvironmentSecretProvider  — reads from env vars (K8s Secret / .env)
├── FileSecretProvider         — reads from Vault-mounted files, supports rotation
└── ChainedSecretProvider      — tries file first, falls back to env
```

### Audit Logging
Every secret access is logged via structured logging:
- **Logged:** secret key name, provider type, access timestamp
- **Never logged:** secret values

### Connection String Injection
Database connection strings are injected via environment variables
(`MemoryPersistence__ConnectionString`) set by Kubernetes Secrets or docker-compose
`.env` substitution. No connection strings exist in `appsettings.json`.

## GitHub Actions Secrets

The CI/CD pipeline requires these GitHub repository secrets:

| Secret | Purpose |
|--------|---------|
| `GITHUB_TOKEN` | Auto-provided, used for GHCR image push |

For deployment workflows (not included in base CI/CD), configure:

| Secret | Purpose |
|--------|---------|
| `ARCHONAI_JWT_SIGNING_KEY` | JWT signing key for deployed environments |
| `POSTGRES_PASSWORD` | Database password |
| `OPENAI_API_KEY` | OpenAI API key (if using OpenAI provider) |
| `ANTHROPIC_API_KEY` | Anthropic API key (if using Anthropic provider) |

## Security Checklist

- [ ] No secrets in `values.yaml`, `appsettings.json`, or `docker-compose.yml`
- [ ] `.env` files are in `.gitignore` and `.dockerignore`
- [ ] Helm releases use `--set` for secrets (never `values.yaml`)
- [ ] CI/CD pipeline includes secret scanning step
- [ ] JWT key rotation uses dual-key validation
- [ ] All secret accesses are audit-logged (values never logged)
- [ ] Container images run as non-root with read-only root filesystem
- [ ] Network policies restrict pod-to-pod communication

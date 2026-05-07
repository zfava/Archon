# Secret Boundary Model

## Overview

ArchonAI enforces a clear boundary between secrets that the platform manages and secrets that the customer controls. The boundary shifts depending on the deployment topology.

## Secret Categories

| Secret | SaaS (Operator-Managed) | Single-Tenant (Operator-Managed) | Private (Customer-Managed) |
|---|---|---|---|
| JWT signing key | Operator | Operator (per tenant) | Customer |
| PostgreSQL credentials | Operator | Operator (per tenant) | Customer |
| NATS credentials | Operator | Operator (per tenant) | Customer |
| Model provider API keys | Operator | Operator or Customer | Customer |
| Connector credentials (Salesforce, HubSpot, etc.) | Customer (per tenant) | Customer | Customer |
| TLS certificates | Operator (cloud LB) | Operator (ingress) | Customer |

**Operator** = ArchonAI operations team.
**Customer** = the enterprise buyer's security/infra team.

## Secret Injection Mechanisms

### 1. Inline (Default)

Secrets are provided as Helm `--set` values and rendered into Kubernetes Secrets:

```bash
helm install archonai ./deploy/helm/archonai \
  --set security.jwtSigningKey="$(openssl rand -base64 48)" \
  --set postgres.password="$(openssl rand -base64 24)"
```

The Helm chart renders a standard `v1/Secret` resource with `stringData`. This is appropriate for operator-managed deployments where the operator controls the cluster.

**Limitations:** Secrets are visible in Helm release history. Use `helm secrets` plugin or Sealed Secrets for additional protection in SaaS/single-tenant deployments.

### 2. External Secrets Operator

Secrets are fetched from an external vault and synced into Kubernetes Secrets:

```yaml
secrets:
  provider: external-secrets
  externalSecrets:
    enabled: true
    secretStoreRef:
      name: aws-secrets-manager
      kind: ClusterSecretStore
    remoteRefs:
      jwtSigningKey: "archonai/prod/jwt-key"
      postgresUsername: "archonai/prod/db-user"
      postgresPassword: "archonai/prod/db-pass"
      modelProviderApiKey: "archonai/prod/openai-key"
    refreshInterval: 1h
```

When `secrets.provider=external-secrets`, the Helm chart renders `ExternalSecret` resources (API version `external-secrets.io/v1beta1`) instead of inline Kubernetes Secrets. The External Secrets Operator syncs them into standard Secrets that pods consume via `envFrom`.

**Supported backends:** AWS Secrets Manager, Azure Key Vault, GCP Secret Manager, HashiCorp Vault, IBM Secrets Manager, Oracle Vault.

### 3. Vault Injector (Future)

For environments using HashiCorp Vault Agent Injector, a `vault-injector` provider option is reserved. This will annotate pods with `vault.hashicorp.com/agent-inject` annotations to sidecar-inject secrets directly into the pod filesystem. Not yet implemented.

## Secret Lifecycle

### Rotation

| Mechanism | Rotation Path |
|---|---|
| Inline | Re-run `helm upgrade` with new `--set` values. Pods restart on Secret change via ConfigMap hash annotation. |
| External Secrets | Rotate in the external vault. ESO auto-syncs on `refreshInterval`. Pods pick up new values on next restart or via a reloader. |

### Deletion

Secrets created by the Helm chart are deleted when the release is uninstalled (`helm uninstall`). External Secrets are cleaned up via `creationPolicy: Owner` — the `ExternalSecret` resource owns the target Secret.

## Secrets in Environment Variables

All ArchonAI services consume secrets via environment variables injected from Kubernetes Secrets. The mapping:

| Kubernetes Secret Key | Environment Variable | Consumed By |
|---|---|---|
| `ARCHONAI_JWT_SIGNING_KEY` | `ARCHONAI_JWT_SIGNING_KEY` | Gateway, API |
| `username` (postgres-credentials) | `POSTGRES_USER` | PostgreSQL pod |
| `password` (postgres-credentials) | `POSTGRES_PASSWORD` | PostgreSQL pod |
| `ModelProviders__OpenAI__ApiKey` | `ModelProviders__OpenAI__ApiKey` | API, Workers |

Connection strings (containing embedded credentials) are set via ConfigMap environment variables. In private deployments, the connection string should reference an external database endpoint where credentials are managed by the customer's IAM or vault.

## What Is NOT Stored as Secrets

These are configuration values, not secrets, and are stored in ConfigMaps:

- JWT issuer / audience strings
- NATS URL (without credentials)
- Postgres host / port / database name (without credentials)
- Environment name (`Production`, `Staging`)
- Service-specific tuning (parallelism, retry counts, etc.)

## Security Hardening Checklist

| Control | SaaS | Single-Tenant | Private |
|---|---|---|---|
| Secrets encrypted at rest (etcd) | Operator responsibility | Operator responsibility | Customer responsibility |
| Network policies enabled | Yes (values-saas.yaml) | Yes (values-single-tenant.yaml) | Yes (values-private.yaml) |
| Pod security: non-root, read-only rootfs, drop ALL caps | All templates | All templates | All templates |
| Image pull from private registry | Optional | Optional | Required |
| External secret store | Recommended | Recommended | Required |
| Secret rotation automation | Operator SOP | Operator SOP | Customer SOP |
| Helm release history encryption | Recommended (helm-secrets) | Recommended (helm-secrets) | Customer responsibility |

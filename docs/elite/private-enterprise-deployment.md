# Private Enterprise Deployment

## Overview

Private deployment places ArchonAI entirely within a customer's infrastructure boundary — their Kubernetes cluster, their VPC, their secret store, their container registry. No ArchonAI-operated infrastructure is involved in the data path.

This document covers the requirements, configuration, and operational handoff for private deployments.

## Prerequisites

The customer must provide:

| Component | Requirement | Notes |
|---|---|---|
| **Kubernetes cluster** | v1.26+ with NetworkPolicy support | EKS, GKE, AKS, or on-premise |
| **PostgreSQL** | v15+ with pgvector extension | RDS, Cloud SQL, Azure DB, or self-managed |
| **NATS** | v2.10+ with JetStream enabled | Customer-managed or NATS operator |
| **Container registry** | Private registry accessible from cluster | ECR, GCR, ACR, Harbor, etc. |
| **External secret store** | One of: AWS Secrets Manager, Azure Key Vault, GCP Secret Manager, HashiCorp Vault | With External Secrets Operator installed in cluster |
| **Ingress controller** | NGINX, Traefik, AWS ALB, or equivalent | For TLS termination and routing |

## Image Delivery

ArchonAI images are published to GHCR (`ghcr.io`) during CI/CD. For private deployments, images must be mirrored to the customer's registry:

```bash
# Example: mirror to ECR
IMAGES=(api gateway runtime scheduler agents)
SOURCE=ghcr.io/archonai
TARGET=123456789.dkr.ecr.us-east-1.amazonaws.com/archonai

for img in "${IMAGES[@]}"; do
  docker pull $SOURCE/archonai-$img:1.0.0
  docker tag  $SOURCE/archonai-$img:1.0.0 $TARGET-$img:1.0.0
  docker push $TARGET-$img:1.0.0
done
```

Configure the Helm chart to use the private registry:

```yaml
# customer-overrides.yaml
global:
  imageRegistry: "123456789.dkr.ecr.us-east-1.amazonaws.com"
  imagePullSecrets:
    - name: archonai-registry-credentials
```

## Secret Configuration

Private deployments use the External Secrets Operator to pull secrets from the customer's vault:

```yaml
# customer-overrides.yaml
secrets:
  provider: external-secrets
  externalSecrets:
    enabled: true
    secretStoreRef:
      name: aws-secrets-manager    # or: azure-keyvault, gcp-secret-manager, vault
      kind: ClusterSecretStore
    remoteRefs:
      jwtSigningKey: "archonai/production/jwt-signing-key"
      postgresUsername: "archonai/production/db-username"
      postgresPassword: "archonai/production/db-password"
      modelProviderApiKey: "archonai/production/openai-api-key"
    refreshInterval: 1h
```

The Helm chart renders `ExternalSecret` resources instead of inline Kubernetes Secrets when `secrets.provider=external-secrets`.

### Secret Store Setup

The customer must create a `SecretStore` or `ClusterSecretStore` in their cluster. Example for AWS:

```yaml
apiVersion: external-secrets.io/v1beta1
kind: ClusterSecretStore
metadata:
  name: aws-secrets-manager
spec:
  provider:
    aws:
      service: SecretsManager
      region: us-east-1
      auth:
        jwt:
          serviceAccountRef:
            name: external-secrets-sa
            namespace: external-secrets
```

## Infrastructure Configuration

Since `topology.bundledInfra=false`, the customer provides connection details for Postgres and NATS:

```yaml
# customer-overrides.yaml
api:
  env:
    MemoryPersistence__ConnectionString: "Host=archonai-db.internal;Port=5432;Database=archonai;Username=ref:secret;Password=ref:secret"
    EventBus__Url: "nats://nats.internal:4222"

runtime:
  env:
    MemoryPersistence__ConnectionString: "Host=archonai-db.internal;Port=5432;Database=archonai;Username=ref:secret;Password=ref:secret"
    EventBus__Url: "nats://nats.internal:4222"

scheduler:
  env:
    MemoryPersistence__ConnectionString: "Host=archonai-db.internal;Port=5432;Database=archonai;Username=ref:secret;Password=ref:secret"
    EventBus__Url: "nats://nats.internal:4222"

agents:
  env:
    MemoryPersistence__ConnectionString: "Host=archonai-db.internal;Port=5432;Database=archonai;Username=ref:secret;Password=ref:secret"
    EventBus__Url: "nats://nats.internal:4222"
```

For connection strings that contain credentials, the recommended approach is to use the External Secrets Operator to inject the full connection string as a secret, then reference it via `secretRef` in the pod spec. The current chart uses `envFrom` to load secrets, so credentials in connection strings are covered when the ExternalSecret populates the target Kubernetes Secret.

## Network Controls

Private deployments should restrict egress to known endpoints:

```yaml
# customer-overrides.yaml
networkPolicy:
  enabled: true
  ingressCIDRs:
    - "10.0.0.0/8"        # corporate network
  egressCIDRs:
    - "52.0.0.0/8"        # model provider IP range (example)
    - "10.0.0.0/8"        # internal services
```

When `egressCIDRs` is non-empty, only HTTPS (443) traffic to those CIDRs is permitted. DNS (53) is always allowed within the cluster.

## Installation

```bash
helm install archonai ./deploy/helm/archonai \
  -n archonai --create-namespace \
  -f ./deploy/helm/archonai/values-private.yaml \
  -f ./customer-overrides.yaml
```

## Verification Checklist

After installation, verify:

| Check | Command |
|---|---|
| Pods running | `kubectl get pods -n archonai` |
| Gateway health | `curl -k https://<ingress>/health` |
| API health | `kubectl exec deploy/archonai-gateway -- curl http://archonai-api:80/api/v1/health` |
| Secrets populated | `kubectl get secret archonai-secrets -n archonai -o yaml` |
| Network policies active | `kubectl get networkpolicy -n archonai` |
| External secrets syncing | `kubectl get externalsecret -n archonai` |
| PDBs configured | `kubectl get pdb -n archonai` |

## Operational Handoff

For private deployments, the customer's operations team owns:

- Kubernetes cluster operations (upgrades, node scaling, monitoring)
- Database administration (backups, failover, connection pooling)
- Secret rotation (via their vault; External Secrets Operator auto-syncs)
- TLS certificate management (ingress controller)
- Network policy tuning (CIDR allowlists for new model providers)
- Image updates (pull new versions from GHCR, push to private registry)

ArchonAI provides:

- Helm chart releases with versioned upgrade paths
- Container images for each release
- Migration scripts when schema changes are required
- Support for configuration questions and deployment troubleshooting

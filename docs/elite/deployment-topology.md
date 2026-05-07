# Deployment Topology

## Overview

ArchonAI supports three deployment topologies, each backed by a dedicated Helm values overlay. The topology is selected at install time and controls namespace isolation, infrastructure bundling, secret sourcing, network policy enforcement, and scaling defaults.

## Supported Topologies

| Topology | Overlay File | Namespace | Infrastructure | Secrets | Network Policies |
|---|---|---|---|---|---|
| **Shared SaaS** | `values-saas.yaml` | `archonai` (shared) | Bundled Postgres + NATS | Inline or external | Enabled |
| **Single-Tenant Hosted** | `values-single-tenant.yaml` | `archonai-<customer>` (dedicated) | Bundled per tenant | Inline or external | Enabled |
| **Private / VPC** | `values-private.yaml` | `archonai` (customer-controlled) | Customer-provided | External secret store | Enabled + CIDR-restricted |

## Architecture by Topology

### Shared SaaS

```
Internet → Cloud LB → Gateway (2) → API (2) → Postgres (shared)
                                            → NATS (shared)
                         Runtime (2) ───────┘
                         Scheduler (1) ─────┘
                         Agents (3) ────────┘
```

- All tenants share a single Kubernetes namespace and a single Postgres instance
- Tenant isolation enforced at application level via `tenant_id` scoping on every query
- Multi-tenant data stored in the same schema (application-level row isolation)
- Network policies restrict inter-service traffic to necessary paths only
- Pod disruption budgets ensure availability during rolling updates

**Install:**

```bash
helm install archonai ./deploy/helm/archonai \
  -f ./deploy/helm/archonai/values-saas.yaml \
  --set security.jwtSigningKey="$(openssl rand -base64 48)" \
  --set postgres.password="$(openssl rand -base64 24)"
```

### Single-Tenant Hosted

```
Customer Ingress → Gateway (2) → API (2) → Postgres (dedicated)
                                         → NATS (dedicated)
                      Runtime (1) ───────┘
                      Scheduler (1) ─────┘
                      Agents (2) ────────┘
```

- Each customer gets a dedicated Kubernetes namespace (`archonai-<customer>`)
- Full infrastructure isolation: dedicated Postgres, NATS, and all services per customer
- Gateway exposed as ClusterIP, fronted by a shared ingress controller
- Operator manages N parallel installations — one `helm install` per customer

**Install:**

```bash
helm install archonai-acme ./deploy/helm/archonai \
  -n archonai-acme --create-namespace \
  -f ./deploy/helm/archonai/values-single-tenant.yaml \
  --set security.jwtSigningKey="$(openssl rand -base64 48)" \
  --set postgres.password="$(openssl rand -base64 24)"
```

### Private / VPC Deployment

```
Customer Network → Customer Ingress → Gateway (2) → API (3)
                                                   ↓
                       Customer-managed Postgres ←─┤
                       Customer-managed NATS ←─────┤
                       Runtime (2) ←───────────────┤
                       Scheduler (1) ←─────────────┤
                       Agents (3) ←────────────────┘
```

- Deployed into customer's own Kubernetes cluster or VPC
- No bundled infrastructure — expects customer-provisioned Postgres (RDS, Cloud SQL, Azure DB) and NATS
- All secrets sourced from customer's external secret store (AWS Secrets Manager, Azure Key Vault, HashiCorp Vault)
- Images pulled from customer's private container registry
- Network policies enforce explicit CIDR allowlists for egress (model providers, external APIs)

**Install:**

```bash
helm install archonai ./deploy/helm/archonai \
  -n archonai --create-namespace \
  -f ./deploy/helm/archonai/values-private.yaml \
  -f ./customer-overrides.yaml
```

## Service Topology

All topologies deploy the same six services:

| Service | Role | Protocol | Port |
|---|---|---|---|
| **Gateway** | YARP reverse proxy, JWT validation, rate limiting | HTTP | 8080 |
| **API** | Core business logic, REST endpoints, health checks | HTTP | 8080 |
| **Runtime** | Task execution, agent hosting, workflow execution | Worker | — |
| **Scheduler** | Task distribution, workflow coordination, rebalancing | Worker | — |
| **Agents** | Domain-specific agent hosting (Finance, Sales, Ops, etc.) | Worker | — |
| **PostgreSQL** | Persistence layer with pgvector (bundled topologies only) | TCP | 5432 |
| **NATS** | Event bus with JetStream (bundled topologies only) | TCP | 4222 |

## Network Flow

When `networkPolicy.enabled=true`:

```
Gateway ─→ API (8080)
API ─→ Postgres (5432), NATS (4222), HTTPS egress (443)
Runtime ─→ Postgres (5432), NATS (4222), HTTPS egress (443)
Scheduler ─→ Postgres (5432), NATS (4222), HTTPS egress (443)
Agents ─→ Postgres (5432), NATS (4222), HTTPS egress (443)
Postgres ─→ (no egress)
NATS ─→ (no egress)
```

Workers accept no ingress traffic. Postgres and NATS accept connections only from pods labeled `app.kubernetes.io/part-of: archonai`.

## Topology Selection in values.yaml

```yaml
topology:
  mode: saas | single-tenant | private
  tenantIsolation: shared | schema | dedicated
  bundledInfra: true | false
```

- `bundledInfra: false` suppresses rendering of the Postgres and NATS deployment templates
- `tenantIsolation` documents the isolation level but does not change runtime behavior (application-level row isolation is always enforced)
- `mode` is a documentation/labeling field used for operational clarity

## Configuration Precedence

Configuration flows in this order (last wins):

1. `appsettings.json` (compiled defaults)
2. `appsettings.Production.json` (environment-specific)
3. Helm ConfigMap (environment variables via `envFrom`)
4. Helm Secret (sensitive environment variables via `envFrom`)
5. Pod-level `--set` overrides

Environment variables use `__` as the nested key separator (e.g., `EventBus__Url` maps to `EventBus:Url` in .NET configuration).

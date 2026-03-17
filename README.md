# ArchonAI

## ArchonAI Platform Architecture

See the full enterprise architecture proposal in [`ARCHITECTURE.md`](./ARCHITECTURE.md).

## Infrastructure

The repository includes deployable infrastructure assets for local and cluster execution:

- Docker image build: `archonai/deploy/docker/Dockerfile.api`
- Local stack: `archonai/docker-compose.yml` (API + PostgreSQL/pgvector + NATS)
- Kubernetes manifests: `archonai/deploy/kubernetes/*.yaml`
- Helm chart: `archonai/deploy/helm/archonai`

## Scalability refactor highlights

- **Versioned APIs** under `/api/v1` and `/api/v2` for long-term compatibility.
- **Extensible plugin architecture** using configurable assembly loading for agent and tool plugins.
- **Upgradeable runtime settings** via the `Runtime` config section (parallelism, retries, sharding).
- **Sharded task queues** in runtime execution manager to reduce contention and support high agent volume.
- **Deterministic workflow state machine** in `ArchonAI.Workflow` (`Created -> Planning -> Scheduled -> Executing -> Evaluating -> Completed/Failed -> Escalated`).
- **Service isolation** maintained through modular projects and deployable components (API, NATS, Postgres).
- **Agent capability registry APIs** under `/api/v1/registry` for querying agent identities, capabilities, tools, permissions, latency, and cost metrics.
- **Knowledge graph layer** (`ArchonAI.Knowledge`) for relationships between agents, tasks, systems, data sources, and events, exposed to planners and agents via `knowledge.query`.
- **Evaluation engine** (`ArchonAI.Evaluation`) for scoring results, detecting failure patterns, measuring agent performance, and recording execution metrics into memory.
- **Policy and safety engine** (`ArchonAI.Policy`) for permission enforcement, risk scoring, approval workflows, and runtime guardrails.
- **Simulation engine** (`ArchonAI.Simulation`) to simulate workflows, predict outcomes, and compare strategies before execution.

### Local run

From the repository root:

```bash
docker compose -f archonai/docker-compose.yml up --build
```
